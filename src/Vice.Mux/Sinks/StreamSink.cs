using Vice.Logging;

namespace Vice.Mux.Sinks;

internal sealed class StreamSink : StreamBackedSink
{
    public StreamSink(Stream stream, string label, IViceLogger logger)
        : base(stream, label, logger)
    {
    }
}
