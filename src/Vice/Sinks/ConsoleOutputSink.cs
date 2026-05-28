namespace Vice;

internal sealed class ConsoleOutputSink : IOutputSink
{
    public void Line(string text) => System.Console.Out.WriteLine(text);
    public void Line() => System.Console.Out.WriteLine();
    public void Write(string text) => System.Console.Out.Write(text);
    public void Error(string text) => System.Console.Error.WriteLine(text);
}
