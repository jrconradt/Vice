using System.IO.Pipes;
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
