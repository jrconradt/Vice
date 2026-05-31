using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Vice.Logging;

namespace Vice.Ipc;

internal sealed class PipeServer : IPipeServer
{
    private const int MaxConcurrentClients = 64;
    private const int SingleServerInstance = 1;
    private static readonly TimeSpan ClientDisposeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AcceptIterationBackoff = TimeSpan.FromMilliseconds(250);

    private readonly string _pipeName;
    private readonly Func<PipeMessage, CancellationToken, Task<PipeMessage?>> _messageHandler;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, Task> _clientTasks = new();
    private int _nextClientId;
    private Task? _acceptLoop;
    private CancellationTokenSource? _linkedCts;
    private volatile bool _isListening;
    private volatile bool _acceptLoopCrashed;
    private Exception? _faulted;

    public PipeServer(
        string pipeName,
        Func<PipeMessage, CancellationToken, Task<PipeMessage?>> messageHandler)
    {
        _pipeName = pipeName;
        _messageHandler = messageHandler;
    }

    public bool IsListening => _isListening;

    public bool AcceptLoopCrashed => _acceptLoopCrashed;

    public Exception? Faulted => _faulted;

    public Task StartAsync(CancellationToken ct)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        _linkedCts = linkedCts;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _acceptLoop = Task.Run(() => AcceptLoopAsync(linkedCts.Token, ready), linkedCts.Token);

        return ready.Task;
    }

    private async Task AcceptLoopAsync(CancellationToken ct, TaskCompletionSource ready)
    {
        _isListening = true;
        var readyCompleted = false;
        var everBound = false;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? serverStream = null;

                try
                {
                    serverStream = CreateServerStream();
                    everBound = true;

                    if (!readyCompleted)
                    {
                        ready.TrySetResult();
                        readyCompleted = true;
                    }

                    try
                    {
                        await WaitForConnectionWithRestrictiveUmaskAsync(serverStream, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        await serverStream.DisposeAsync().ConfigureAwait(false);
                        serverStream = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Vice.Log.Emit(ViceLogLevel.Warn, $"pipe wait-for-connection failed on '{_pipeName}'", ex);
                        await serverStream.DisposeAsync().ConfigureAwait(false);
                        serverStream = null;
                        continue;
                    }

                    var peerCheck = VerifyPeer(serverStream);
                    if (!peerCheck.Authorized)
                    {
                        await serverStream.DisposeAsync().ConfigureAwait(false);
                        serverStream = null;
                        continue;
                    }

                    if (_clientTasks.Count >= MaxConcurrentClients)
                    {
                        var capMessage =
                            $"pipe server '{_pipeName}' at concurrent-client cap ({MaxConcurrentClients}); rejecting connection peer-uid={peerCheck.PeerUid} peer-pid={peerCheck.PeerPid}";
                        Vice.Log.Emit(ViceLogLevel.Warn, capMessage);
                        EmitAudit(capMessage);
                        await serverStream.DisposeAsync().ConfigureAwait(false);
                        serverStream = null;
                        continue;
                    }

                    var attached = serverStream;
                    serverStream = null;
                    var clientId = Interlocked.Increment(ref _nextClientId);
                    EmitAudit(
                        $"pipe connection accepted on '{_pipeName}': client={clientId} peer-uid={peerCheck.PeerUid} peer-pid={peerCheck.PeerPid}");
                    var clientTask = HandleClientAsync(attached, clientId, ct);

                    _clientTasks[clientId] = clientTask;
                }
                catch (Exception ex)
                {
                    if (serverStream is not null)
                    {
                        try
                        {
                            await serverStream.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception disposeEx)
                        {
                            Vice.Log.Emit(ViceLogLevel.Warn, "pipe server stream dispose failed", disposeEx);
                        }
                    }

                    if (!everBound)
                    {
                        var bindMessage =
                            $"pipe server '{_pipeName}' could not bind (another daemon may already own this pipe); not starting a second listener";
                        Vice.Log.Emit(ViceLogLevel.Error, bindMessage, ex);
                        EmitAudit(bindMessage);
                        _acceptLoopCrashed = true;
                        _faulted = ex;

                        if (!readyCompleted)
                        {
                            ready.TrySetException(ex);
                            readyCompleted = true;
                        }

                        break;
                    }

                    Vice.Log.Emit(ViceLogLevel.Error,
                        $"pipe accept loop iteration failed on '{_pipeName}'; backing off and retrying", ex);

                    try
                    {
                        await Task.Delay(AcceptIterationBackoff, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Vice.Log.Emit(ViceLogLevel.Trace, $"pipe accept loop cancelled on '{_pipeName}'");
        }
        catch (Exception ex)
        {
            _acceptLoopCrashed = true;
            _faulted = ex;
            Vice.Log.Emit(ViceLogLevel.Error, $"pipe accept loop crashed on '{_pipeName}'", ex);

            if (!readyCompleted)
            {
                ready.TrySetException(ex);
                readyCompleted = true;
            }
        }
        finally
        {
            _isListening = false;

            if (!readyCompleted)
            {
                ready.TrySetResult();
            }
        }
    }

    private NamedPipeServerStream CreateServerStream()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return CreateWindowsAclStream();
        }

        var maskApplied = false;
        var previousMask = 0u;
        try
        {
            previousMask = Umask(UnixUmaskUserOnly);
            maskApplied = true;
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn,
                "umask P/Invoke failed; pipe socket may be created with default permissions", ex);
        }

        try
        {
            var stream = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                SingleServerInstance,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            TryRestrictUnixPipePermissions();
            return stream;
        }
        finally
        {
            if (maskApplied)
            {
                try
                {
                    _ = Umask(previousMask);
                }
                catch (Exception ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, "umask restore failed", ex);
                }
            }
        }
    }

    private async Task WaitForConnectionWithRestrictiveUmaskAsync(
        NamedPipeServerStream stream, CancellationToken ct)
    {
        await stream.WaitForConnectionAsync(ct).ConfigureAwait(false);
    }

    [UnsupportedOSPlatform("windows")]
    private void TryRestrictUnixPipePermissions()
    {
        var socketPath = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + _pipeName);
        try
        {
            if (File.Exists(socketPath))
            {
                File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, $"failed to restrict permissions on {socketPath}", ex);
        }
    }

    private const uint UnixUmaskUserOnly = 0x3F;

    [DllImport("libc", EntryPoint = "umask")]
    private static extern uint Umask(uint mask);

    private readonly record struct PeerCheck(bool Authorized, int PeerUid, int PeerPid);

    private PeerCheck VerifyPeer(NamedPipeServerStream stream)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new PeerCheck(true, -1, -1);
        }

        var gotEuid = PeerCredentials.TryGetEuid(out var euid);
        if (!gotEuid)
        {
            var message = $"pipe peer-uid check: geteuid failed on '{_pipeName}'; rejecting connection";
            Vice.Log.Emit(ViceLogLevel.Warn, message);
            EmitAudit(message);
        }

        var gotPeer = PeerCredentials.TryGetPeerCredentials(stream.SafePipeHandle, out var peerUid, out var peerPid);
        if (gotEuid && !gotPeer)
        {
            var message = $"pipe peer-uid check: unable to determine peer uid on '{_pipeName}'; rejecting";
            Vice.Log.Emit(ViceLogLevel.Warn, message);
            EmitAudit(message);
        }

        if (gotEuid
            && gotPeer
            && peerUid != euid)
        {
            var message =
                $"pipe peer-uid mismatch on '{_pipeName}': peer-uid={peerUid} peer-pid={peerPid} euid={euid}; rejecting";
            Vice.Log.Emit(ViceLogLevel.Warn, message);
            EmitAudit(message);
        }

        var authorized = AuthorizePeer(peerUid, euid, gotEuid, gotPeer);
        return new PeerCheck(authorized, peerUid, peerPid);
    }

    private static void EmitAudit(string message)
    {
        Vice.Log.Audit.Log(ViceLogLevel.Warn, message);
    }

    internal static bool AuthorizePeer(int peerUid, int euid, bool gotEuid, bool gotPeer)
    {
        if (!gotEuid)
        {
            return false;
        }

        if (!gotPeer)
        {
            return false;
        }

        return peerUid == euid;
    }

    [SupportedOSPlatform("windows")]
    private NamedPipeServerStream CreateWindowsAclStream()
    {
        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot resolve current Windows user SID for pipe ACL.");

        security.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.ReadWrite,
            System.Security.AccessControl.AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            SingleServerInstance,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, int clientId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                PipeMessage? message;

                try
                {
                    message = await PipeProtocol.ReadMessageAsync(stream, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Debug, "pipe client read failed", ex);
                    break;
                }

                if (message is null)
                {
                    break;
                }

                if (message is CommandMessage commandMessage)
                {
                    EmitAudit(
                        $"pipe command on '{_pipeName}': client={clientId} command={commandMessage.CommandLine}");
                }

                PipeMessage? response;

                try
                {
                    response = await _messageHandler(message, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, "pipe message handler threw", ex);
                    response = new CommandResponse
                    {
                        ExitCode = 1,
                        Output = string.Empty,
                        Error = ex.Message,
                    };
                }

                if (response is not null)
                {
                    try
                    {
                        await PipeProtocol.WriteMessageAsync(stream, response, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (IOException ex)
                    {
                        Vice.Log.Emit(ViceLogLevel.Debug, "pipe client write failed", ex);
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "pipe client handler threw", ex);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            _clientTasks.TryRemove(clientId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Vice.Log.Emit(ViceLogLevel.Trace, "pipe accept loop cancelled during dispose");
            }
            catch (Exception ex)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, "pipe accept loop faulted during dispose", ex);
            }
        }

        var tasks = _clientTasks.Values.ToArray();

        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks).WaitAsync(ClientDisposeTimeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Vice.Log.Emit(ViceLogLevel.Trace, "pipe client tasks cancelled during dispose");
            }
            catch (TimeoutException)
            {
                Vice.Log.Emit(ViceLogLevel.Warn,
                    $"pipe client tasks did not finish within {ClientDisposeTimeout.TotalSeconds:0.0}s; proceeding with dispose");
            }
            catch (Exception ex)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, "pipe client task faulted during dispose", ex);
            }
        }

        _cts.Dispose();
        _linkedCts?.Dispose();
    }
}
