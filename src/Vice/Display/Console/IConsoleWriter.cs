namespace Vice.Display.Rendering;

public interface IConsoleWriter
{
    void Write(string text);
    void WriteLine(string text);
    void WriteLine();
    void WriteError(string text);
}
