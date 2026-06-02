using Vice.Logging;

namespace Vice.Mux.Sinks;

internal abstract class StreamBackedSink : ISink
{
    private readonly Stream _stream;
    private protected readonly IViceLogger Logger;

    protected StreamBackedSink(Stream stream, string label, IViceLogger logger)
    {
        _stream = stream;
        Label = label;
        Logger = logger ?? NullViceLogger.Instance;
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
            Logger.Log(ViceLogLevel.Warn,
                       $"Sink '{Label}' final flush failed during dispose.",
                       ex);
        }
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Logger.Log(ViceLogLevel.Warn,
                       $"Sink '{Label}' stream dispose failed.",
                       ex);
        }

        await DisposeUnderlyingAsync().ConfigureAwait(false);
    }

    protected virtual ValueTask DisposeUnderlyingAsync()
        => ValueTask.CompletedTask;
}
