namespace Vice.Concurrency;

public static class RetryBackoff
{
    public static TimeSpan Exponential(TimeSpan baseDelay,
                                       TimeSpan maxDelay,
                                       int attempt)
    {
        var scaled = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(scaled, maxDelay.TotalMilliseconds);
        var jittered = capped * (0.5 + Random.Shared.NextDouble() * 0.5);
        return TimeSpan.FromMilliseconds(jittered);
    }
}
