using System.Runtime.CompilerServices;

namespace Vice.Logging;

public sealed class NullViceLogger : IViceLogger
{
    public static readonly NullViceLogger Instance = new();

    private NullViceLogger() { }

    public bool IsEnabled(ViceLogLevel level) => false;

    public void Log(ViceError error) { }

    public void Log(
        ViceLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
    }
}
