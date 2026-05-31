using Vice.Logging;

namespace Vice;

public static class Telemetry
{
    private static FileTelemetrySink? _sink;

    public static void Configure(FileTelemetrySink? sink)
        => Volatile.Write(ref _sink, sink);

    public static FileTelemetrySink? Current => Volatile.Read(ref _sink);

    public static bool IsEnabled => Volatile.Read(ref _sink) is { IsEnabled: true };

    public static void Track(string eventName, IReadOnlyDictionary<string, string>? properties = null)
        => Volatile.Read(ref _sink)?.Track(eventName, properties);

    public static void TrackException(Exception ex, IReadOnlyDictionary<string, string>? properties = null)
        => Volatile.Read(ref _sink)?.TrackException(ex, properties);

    public static Task FlushAsync()
    {
        var sink = Volatile.Read(ref _sink);
        return sink is null ? Task.CompletedTask : sink.FlushAsync();
    }
}
