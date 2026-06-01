namespace Vice.Mux.Sinks;

internal sealed class StreamSink : StreamBackedSink
{
    public StreamSink(Stream stream, string label)
        : base(stream, label)
    {
    }
}
