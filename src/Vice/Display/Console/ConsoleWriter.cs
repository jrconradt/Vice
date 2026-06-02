using Vice.Display.Rendering;

namespace Vice.Display;

internal sealed class ConsoleWriter : IConsoleWriter
{
    private readonly IOutputSink _sink;

    public ConsoleWriter(IOutputSink sink)
    {
        _sink = sink ?? NullOutputSink.Instance;
    }

    public void Write(string text) => _sink.Write(text);
    public void WriteLine(string text) => _sink.Line(text);
    public void WriteLine() => _sink.Line();
    public void WriteError(string text) => _sink.Error(text);
}
