using Vice;
using Vice.Logging;

namespace Vice.Mux.Sinks;

internal abstract class SinkBase : ISink
{
    private readonly Stream _stream;

    protected SinkBase(Stream stream, string label)
    {
        _stream = stream;
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
        await DisposeCoreAsync().ConfigureAwait(false);
    }

    protected virtual ValueTask DisposeCoreAsync() => ValueTask.CompletedTask;
}
