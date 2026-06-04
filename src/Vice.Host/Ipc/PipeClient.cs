using System.IO.Pipes;
using System.Runtime.InteropServices;
using Vice.Logging;

namespace Vice.Ipc;

internal sealed class PipeClient : IPipeClient
{
    private readonly NamedPipeClientStream _stream;

    private PipeClient(NamedPipeClientStream stream)
    {
        _stream = stream;
    }

    public bool IsConnected => _stream.IsConnected;

    public static async Task<PipeClient?> TryConnectAsync(
        string pipeName,
        int timeoutMs = 1000,
        IViceLogger? logger = null,
        CancellationToken ct = default)
    {
        var log = logger ?? NullViceLogger.Instance;

        var stream = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await stream.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
            VerifyServerIdentity(stream, pipeName, log);
            return new PipeClient(stream);
        }
        catch (TimeoutException)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            log.Log(ViceLogLevel.Warn, $"permission denied connecting to daemon pipe '{pipeName}'", ex);
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (IOException ex)
        {
            log.Log(ViceLogLevel.Warn, $"pipe IO error connecting to '{pipeName}'", ex);
            await stream.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void VerifyServerIdentity(
        NamedPipeClientStream stream,
        string pipeName,
        IViceLogger log)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var gotEuid = PeerCredentials.TryGetEuid(out var euid);
        if (!gotEuid)
        {
            var message = $"server-identity check: geteuid failed connecting to daemon pipe '{pipeName}'; refusing to send";
            log.Log(ViceLogLevel.Error, message);
            throw new UnauthorizedAccessException(message);
        }

        var gotPeer = PeerCredentials.TryGetPeerUid(stream.SafePipeHandle, out var peerUid);
        if (!gotPeer)
        {
            var message = $"server-identity check: unable to determine server uid on '{pipeName}'; refusing to send";
            log.Log(ViceLogLevel.Error, message);
            throw new UnauthorizedAccessException(message);
        }

        if (peerUid != euid)
        {
            var message = $"server-identity mismatch on '{pipeName}': server-uid={peerUid} euid={euid}; refusing to send";
            log.Log(ViceLogLevel.Error, message);
            throw new UnauthorizedAccessException(message);
        }
    }

    public async Task<PipeMessage?> SendAsync(PipeMessage message, CancellationToken ct)
    {
        await PipeProtocol.WriteMessageAsync(_stream, message, ct).ConfigureAwait(false);
        return await PipeProtocol.ReadMessageAsync(_stream, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return _stream.DisposeAsync();
    }
}
