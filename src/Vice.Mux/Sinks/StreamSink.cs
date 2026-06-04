using Vice.Logging;

namespace Vice.Mux.Sinks;

public sealed class StreamSink : StreamBackedSink
{
    private readonly IDisposable? _owner;

    public StreamSink(Stream stream, string label, IViceLogger logger, IDisposable? owner = null)
        : base(stream, label, logger)
    {
        _owner = owner;
    }

    protected override ValueTask DisposeUnderlyingAsync()
    {
        _owner?.Dispose();
        return ValueTask.CompletedTask;
    }
}
