namespace Vice.Display;

internal sealed class ConsoleWriter : IConsoleWriter
{
    public void Write(string text) => Vice.Output.Write(text);
    public void WriteLine(string text) => Vice.Output.Line(text);
    public void WriteLine() => Vice.Output.Line();
    public void WriteError(string text) => Vice.Output.Error(text);
}
