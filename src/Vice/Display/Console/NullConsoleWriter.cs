using Vice.Display.Rendering;

namespace Vice.Display;

internal sealed class NullConsoleWriter : IConsoleWriter
{
    public static readonly NullConsoleWriter Instance = new();

    public void Write(string text) { }
    public void WriteLine(string text) { }
    public void WriteLine() { }
    public void WriteError(string text) { }
}
