using System.IO.Pipes;
using Vice.Logging;

namespace Vice.Ipc;

internal sealed class PipeClient : IPipeClient
{
    private static readonly TimeSpan RoundTripTimeout = TimeSpan.FromSeconds(30);

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
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RoundTripTimeout);
        var token = timeoutCts.Token;

        try
        {
            await PipeProtocol.WriteMessageAsync(_stream, message, token).ConfigureAwait(false);
            return await PipeProtocol.ReadMessageAsync(_stream, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"IPC round-trip exceeded {RoundTripTimeout.TotalSeconds:0}s with no response from the daemon.");
        }
    }

    public ValueTask DisposeAsync()
    {
        return _stream.DisposeAsync();
    }
}
