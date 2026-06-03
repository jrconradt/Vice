using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Vice.Contracts;
using Vice.Logging;

namespace Vice.Ipc;

internal sealed class PipeServer : IPipeServer
{
    private const int MaxConcurrentClients = 64;
    private const int MAX_CONSECUTIVE_ACCEPT_FAILURES = 10;
    private static readonly TimeSpan ClientDisposeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AcceptIterationBackoff = TimeSpan.FromMilliseconds(250);

    private readonly string _pipeName;
    private readonly Func<PipeMessage, CancellationToken, Task<PipeMessage?>> _messageHandler;
    private readonly IViceLogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, Task> _clientTasks = new();
    private int _nextClientId;
    private Task? _acceptLoop;
    private CancellationTokenSource? _linkedCts;
    private volatile bool _isListening;
    private volatile bool _acceptLoopCrashed;
    private volatile bool _bindConflicted;
    private Exception? _faulted;

    public PipeServer(
        string pipeName,
        Func<PipeMessage, CancellationToken, Task<PipeMessage?>> messageHandler,
        IViceLogger logger)
    {
        _pipeName = pipeName;
        _messageHandler = messageHandler;
        _logger = logger ?? NullViceLogger.Instance;
    }

    public bool IsListening => _isListening;

    public bool AcceptLoopCrashed => _acceptLoopCrashed;

    public bool BindConflicted => _bindConflicted;

    public Exception? Faulted => _faulted;

    public Task StartAsync(CancellationToken ct)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        _linkedCts = linkedCts;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _acceptLoop = Task.Run(() => AcceptLoopAsync(linkedCts.Token, ready), linkedCts.Token);

        return ready.Task;
    }

    private enum AcceptOutcome
    {
        Continue,
        Stop,
    }

    private sealed class AcceptLoopState
    {
        public bool ReadyCompleted;
        public bool EverBound;
        public int ConsecutiveFailures;
    }

    private async Task AcceptLoopAsync(CancellationToken ct, TaskCompletionSource ready)
    {
        _isListening = true;
        var state = new AcceptLoopState();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var outcome = await AcceptOneAsync(ct, ready, state).ConfigureAwait(false);
                if (outcome == AcceptOutcome.Stop)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Log(ViceLogLevel.Trace, $"pipe accept loop cancelled on '{_pipeName}'");
        }
        catch (Exception ex)
        {
            _acceptLoopCrashed = true;
            _faulted = ex;
            _logger.Log(ViceLogLevel.Error, $"pipe accept loop crashed on '{_pipeName}'", ex);

            if (!state.ReadyCompleted)
            {
                ready.TrySetException(ex);
                state.ReadyCompleted = true;
            }
        }
        finally
        {
            _isListening = false;

            if (!state.ReadyCompleted)
            {
                ready.TrySetResult();
            }
        }
    }

    private async Task<AcceptOutcome> AcceptOneAsync(
        CancellationToken ct,
        TaskCompletionSource ready,
        AcceptLoopState state)
    {
        NamedPipeServerStream? serverStream = null;

        try
        {
            serverStream = CreateServerStream();
            state.EverBound = true;
            state.ConsecutiveFailures = 0;

            if (!state.ReadyCompleted)
            {
                ready.TrySetResult();
                state.ReadyCompleted = true;
            }

            try
            {
                await WaitForConnectionWithRestrictiveUmaskAsync(serverStream, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await serverStream.DisposeAsync().ConfigureAwait(false);
                return AcceptOutcome.Stop;
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Warn, $"pipe wait-for-connection failed on '{_pipeName}'", ex);
                await serverStream.DisposeAsync().ConfigureAwait(false);
                return AcceptOutcome.Continue;
            }

            var peerCheck = VerifyPeer(serverStream);
            if (!peerCheck.Authorized)
            {
                EmitAuditRejection(
                    $"pipe connection rejected on '{_pipeName}': unauthorized peer peer-uid={peerCheck.PeerUid} peer-pid={peerCheck.PeerPid}");
                await serverStream.DisposeAsync().ConfigureAwait(false);
                return AcceptOutcome.Continue;
            }

            if (_clientTasks.Count >= MaxConcurrentClients)
            {
                EmitAuditRejection(
                    $"pipe server '{_pipeName}' at concurrent-client cap ({MaxConcurrentClients}); rejecting connection peer-uid={peerCheck.PeerUid} peer-pid={peerCheck.PeerPid}");
                await serverStream.DisposeAsync().ConfigureAwait(false);
                return AcceptOutcome.Continue;
            }

            var attached = serverStream;
            serverStream = null;
            var clientId = Interlocked.Increment(ref _nextClientId);
            EmitAudit(
                $"pipe connection accepted on '{_pipeName}': client={clientId} peer-uid={peerCheck.PeerUid} peer-pid={peerCheck.PeerPid}");
            var clientGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var clientTask = HandleClientAsync(attached, clientId, clientGate.Task, ct);

            _clientTasks[clientId] = clientTask;
            clientGate.SetResult();
            return AcceptOutcome.Continue;
        }
        catch (Exception ex)
        {
            return await HandleAcceptFailureAsync(ex, serverStream, ct, ready, state).ConfigureAwait(false);
        }
    }

    private async Task<AcceptOutcome> HandleAcceptFailureAsync(
        Exception ex,
        NamedPipeServerStream? serverStream,
        CancellationToken ct,
        TaskCompletionSource ready,
        AcceptLoopState state)
    {
        if (serverStream is not null)
        {
            try
            {
                await serverStream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception disposeEx)
            {
                _logger.Log(ViceLogLevel.Warn, "pipe server stream dispose failed", disposeEx);
            }
        }

        if (!state.EverBound)
        {
            if (IsAddressInUse(ex))
            {
                _logger.Log(ViceLogLevel.Info,
                    $"pipe server '{_pipeName}' already owned by another daemon (address in use); deferring to the existing listener",
                    ex);
                _bindConflicted = true;

                if (!state.ReadyCompleted)
                {
                    ready.TrySetResult();
                    state.ReadyCompleted = true;
                }

                return AcceptOutcome.Stop;
            }

            _logger.Log(ViceLogLevel.Error,
                $"pipe server '{_pipeName}' could not bind (another daemon may already own this pipe); not starting a second listener",
                ex);
            _acceptLoopCrashed = true;
            _faulted = ex;

            if (!state.ReadyCompleted)
            {
                ready.TrySetException(ex);
                state.ReadyCompleted = true;
            }

            return AcceptOutcome.Stop;
        }

        state.ConsecutiveFailures++;

        if (state.ConsecutiveFailures >= MAX_CONSECUTIVE_ACCEPT_FAILURES)
        {
            _logger.Log(ViceLogLevel.Error,
                $"pipe accept loop on '{_pipeName}' failed {state.ConsecutiveFailures} consecutive times; faulting so a supervisor can restart",
                ex);
            _acceptLoopCrashed = true;
            _faulted = ex;
            return AcceptOutcome.Stop;
        }

        _logger.Log(ViceLogLevel.Error,
            $"pipe accept loop iteration failed on '{_pipeName}'; backing off and retrying", ex);

        try
        {
            await Task.Delay(AcceptIterationBackoff, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return AcceptOutcome.Stop;
        }

        return AcceptOutcome.Continue;
    }

    private static bool IsAddressInUse(Exception ex)
    {
        Exception? current = ex;
        while (current is not null)
        {
            if (current is System.Net.Sockets.SocketException socketEx
                && socketEx.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
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
            throw new InvalidOperationException(
                "umask P/Invoke failed; cannot guarantee restrictive pipe-socket permissions, refusing to listen",
                ex);
        }

        try
        {
            var stream = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                MaxConcurrentClients,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            try
            {
                RestrictUnixPipePermissions();
            }
            catch
            {
                stream.Dispose();
                throw;
            }

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
                    _logger.Log(ViceLogLevel.Warn, "umask restore failed", ex);
                }
            }
        }
    }

    private async Task WaitForConnectionWithRestrictiveUmaskAsync(
        NamedPipeServerStream stream, CancellationToken ct)
    {
        using (ct.Register(() => stream.Dispose()))
        {
            try
            {
                await stream.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
        }
    }

    [UnsupportedOSPlatform("windows")]
    private void RestrictUnixPipePermissions()
    {
        var socketPath = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + _pipeName);
        if (!File.Exists(socketPath))
        {
            throw new InvalidOperationException(
                $"pipe socket '{socketPath}' was not present after bind; cannot confirm restrictive permissions, refusing to listen");
        }

        File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var mode = File.GetUnixFileMode(socketPath);
        if (mode != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
        {
            throw new InvalidOperationException(
                $"pipe socket '{socketPath}' has permissions {mode} after tightening; refusing to listen with broader access");
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
            EmitAuditRejection($"pipe peer-uid check: geteuid failed on '{_pipeName}'; rejecting connection");
        }

        var gotPeer = PeerCredentials.TryGetPeerCredentials(stream.SafePipeHandle, out var peerUid, out var peerPid);
        if (gotEuid && !gotPeer)
        {
            EmitAuditRejection($"pipe peer-uid check: unable to determine peer uid on '{_pipeName}'; rejecting");
        }

        if (gotEuid
            && gotPeer
            && peerUid != euid)
        {
            EmitAuditRejection(
                $"pipe peer-uid mismatch on '{_pipeName}': peer-uid={peerUid} peer-pid={peerPid} euid={euid}; rejecting");
        }

        var authorized = AuthorizePeer(peerUid, euid, gotEuid, gotPeer);
        return new PeerCheck(authorized, peerUid, peerPid);
    }

    private void EmitAudit(string message)
    {
        _logger.Log(ViceLogLevel.Info, message);
    }

    private void EmitAuditRejection(string message)
    {
        _logger.Log(ViceLogLevel.Error, message);
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
            MaxConcurrentClients,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream stream,
        int clientId,
        Task gate,
        CancellationToken ct)
    {
        await gate.ConfigureAwait(false);

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
                    _logger.Log(ViceLogLevel.Debug, "pipe client read failed", ex);
                    break;
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.Log(ViceLogLevel.Warn, "pipe client sent malformed frame; rejecting and keeping connection", ex);

                    try
                    {
                        await PipeProtocol.WriteMessageAsync(
                            stream,
                            new CommandResponse
                            {
                                ExitCode = 1,
                                Output = string.Empty,
                                Error = "malformed message frame",
                            },
                            ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (IOException writeEx)
                    {
                        _logger.Log(ViceLogLevel.Debug, "pipe client write failed after malformed frame", writeEx);
                        break;
                    }

                    continue;
                }

                if (message is null)
                {
                    break;
                }

                if (message is CommandMessage commandMessage)
                {
                    var invocationId = Vice.Logging.InvocationScope.Begin();
                    EmitAudit(
                        $"pipe command on '{_pipeName}': client={clientId} invocation={invocationId} command={Vice.Session.InputHistory.Redact(commandMessage.CommandLine)}");
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
                    _logger.Log(ViceLogLevel.Warn, "pipe message handler threw", ex);
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
                        _logger.Log(ViceLogLevel.Debug, "pipe client write failed", ex);
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Log(ViceLogLevel.Warn, "pipe client handler threw", ex);
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
                _logger.Log(ViceLogLevel.Trace, "pipe accept loop cancelled during dispose");
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Warn, "pipe accept loop faulted during dispose", ex);
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
                _logger.Log(ViceLogLevel.Trace, "pipe client tasks cancelled during dispose");
            }
            catch (TimeoutException)
            {
                _logger.Log(ViceLogLevel.Warn,
                    $"pipe client tasks did not finish within {ClientDisposeTimeout.TotalSeconds:0.0}s; proceeding with dispose");
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Warn, "pipe client task faulted during dispose", ex);
            }
        }

        _cts.Dispose();
        _linkedCts?.Dispose();
    }
}
