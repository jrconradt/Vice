namespace Vice.Logging;

public sealed class NullTelemetrySink
{
    public static readonly NullTelemetrySink Instance = new();
    public void Track(string eventName, IReadOnlyDictionary<string, string>? properties = null) { }
    public void TrackException(Exception ex, IReadOnlyDictionary<string, string>? properties = null) { }
}
