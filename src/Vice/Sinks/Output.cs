namespace Vice;

public static class Output
{
    private static IOutputSink _sink = NullOutputSink.Instance;

    public static void Configure(IOutputSink sink)
        => Volatile.Write(ref _sink, sink ?? NullOutputSink.Instance);

    public static IOutputSink Current => Volatile.Read(ref _sink);

    public static void Line(string text) => Volatile.Read(ref _sink).Line(text);
    public static void Line() => Volatile.Read(ref _sink).Line();
    public static void Write(string text) => Volatile.Read(ref _sink).Write(text);
    public static void Error(string text) => Volatile.Read(ref _sink).Error(text);
}
