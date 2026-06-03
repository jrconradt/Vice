namespace Vice.Logging;

public static class LogFormat
{
    public const int MAX_PARAM_REPR = 80;

    public static string Format(ViceError error)
    {
        var text = $"[{error.LogLevel}] [{error.GetType().Name}] [{FormatParams(error.Params)}] {error}";
        if (error.Hint is { } hint)
        {
            text += $"\n  hint: {hint}";
        }
        if (error.InnerException is { } inner)
        {
            text += $"\n  {inner.GetType().Name}: {inner.Message}";
            if (error.LogLevel <= ViceLogLevel.Debug
                && inner.StackTrace is { } st)
            {
                text += $"\n  {st}";
            }
        }
        return text;
    }

    private static string FormatParams(object?[] arguments)
        => string.Join(", ", arguments.Select(Repr));

    private static string Repr(object? p)
    {
        var repr = p?.ToString() ?? "null";
        return repr.Length <= MAX_PARAM_REPR ? repr : repr[..(MAX_PARAM_REPR - 1)] + "…";
    }
}
