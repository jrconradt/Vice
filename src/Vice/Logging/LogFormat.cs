namespace Vice.Logging;

public static class LogFormat
{
    public const int MAX_PARAM_REPR = 80;

    public static string Format(ViceError error)
        => $"[{error.LogLevel}] [{error.GetType().Name}] [{FormatParams(error.Params)}]";

    private static string FormatParams(object?[] params_)
        => string.Join(", ", params_.Select(Repr));

    private static string Repr(object? p)
    {
        var s = p?.ToString() ?? "null";
        return s.Length <= MAX_PARAM_REPR ? s : s[..(MAX_PARAM_REPR - 1)] + "…";
    }
}
