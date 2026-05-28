namespace Vice.Mux.Sinks;

internal sealed class StreamSink : SinkBase
{
    public StreamSink(Stream stream, string label) : base(stream, label) { }
}
