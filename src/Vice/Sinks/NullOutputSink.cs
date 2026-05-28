namespace Vice;

internal sealed class NullOutputSink : IOutputSink
{
    public static readonly NullOutputSink Instance = new();

    private NullOutputSink() { }

    public void Line(string text) { }
    public void Line() { }
    public void Write(string text) { }
    public void Error(string text) { }
}
