namespace Vice.Streaming;

public sealed record BatchOptions(
    int BatchSize = 10,
    TimeSpan? BatchTimeout = null,
    int ChannelCapacity = 100)
{
    public static readonly BatchOptions Default = new();

    public TimeSpan EffectiveTimeout => BatchTimeout ?? TimeSpan.FromMilliseconds(500);
}
