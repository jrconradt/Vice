using System.Net.Sockets;
using Vice.Logging;

namespace Vice.Mux.Sinks;

internal sealed class TcpSink : ISink
{
    private readonly TcpClient _client;
    private readonly Stream _stream;

    public TcpSink(TcpClient client, string label)
    {
        _client = client;
        _stream = client.GetStream();
        Label = label;
    }

    public string Label { get; }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        => _stream.WriteAsync(chunk, ct);

    public ValueTask FlushAsync(CancellationToken ct)
        => new(_stream.FlushAsync(ct));

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Log.Emit(ViceLogLevel.Warn,
                     $"Sink '{Label}' final flush failed during dispose.",
                     ex);
        }
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Log.Emit(ViceLogLevel.Warn,
                     $"Sink '{Label}' stream dispose failed.",
                     ex);
        }
        _client.Dispose();
    }
}
