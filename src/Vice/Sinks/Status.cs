using Vice.Display;

namespace Vice.Core;

public static class Status
{
    private static IStatusSink _sink = NullStatusSink.Instance;

    public static void Configure(IStatusSink sink)
        => Volatile.Write(ref _sink, sink ?? NullStatusSink.Instance);

    public static IStatusSink Current => Volatile.Read(ref _sink);

    public static IStatusHandle Start(string label) => Volatile.Read(ref _sink).Start(label);
}
