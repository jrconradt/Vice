using Vice;
using Vice.Display;

namespace Vice.Tests;

internal sealed class RecordingConsole : IConsoleWriter, IOutputSink, IDisposable
{
    private string _out = "";
    private string _err = "";
    private readonly IOutputSink _previousSink;

    public RecordingConsole()
    {
        _previousSink = Vice.Output.Current;
        Vice.Output.Configure(this);
    }

    public string Output => _out;
    public string Error => _err;

    public void Dispose()
    {
        Vice.Output.Configure(_previousSink);
    }

    public void Reset() { _out = ""; _err = ""; }

    public void Write(string text) => _out += text;
    public void WriteLine(string text) => _out += $"{text}{Environment.NewLine}";
    public void WriteLine() => _out += Environment.NewLine;
    public void WriteError(string text) => _err += $"{text}{Environment.NewLine}";

    void IOutputSink.Line(string text) => WriteLine(text);
    void IOutputSink.Line() => WriteLine();
    void IOutputSink.Write(string text) => Write(text);
    void IOutputSink.Error(string text) => WriteError(text);
}
