namespace Vice.Logging;

public static class LogLevelEnv
{
    public const string VARIABLE = "VICE_LOG_LEVEL";

    public static ViceLogLevel Resolve(string programName)
        => Parse(Environment.GetEnvironmentVariable(VARIABLE), programName);

    public static ViceLogLevel Parse(string? raw, string programName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ViceLogLevel.Info;
        }

        var normalized = raw.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "trace":
                {
                    return ViceLogLevel.Trace;
                }
            case "debug":
                {
                    return ViceLogLevel.Debug;
                }
            case "info":
                {
                    return ViceLogLevel.Info;
                }
            case "warn":
            case "warning":
                {
                    return ViceLogLevel.Warn;
                }
            case "error":
                {
                    return ViceLogLevel.Error;
                }
            default:
                {
                    Console.Error.WriteLine($"{programName}: unknown {VARIABLE} '{raw.Trim()}', defaulting to info.");
                    return ViceLogLevel.Info;
                }
        }
    }
}
