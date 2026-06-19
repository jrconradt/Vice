using Vice.Core;
using Vice.Display.Rendering;

namespace Vice.Tests;

internal sealed class RecordingConsole : IConsoleWriter, IOutputSink, IDisposable
{
    private string _out = "";
    private string _err = "";

    public string Output => _out;
    public string Error => _err;

    public void Dispose()
    {
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
