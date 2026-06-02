using System.Runtime.CompilerServices;

namespace Vice.Logging;

public interface IViceLogger
{
    bool IsEnabled(ViceLogLevel level);

    void Log(ViceError error);

    void Log(
        ViceLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0);

    void Log(
        ViceLogLevel level,
        [InterpolatedStringHandlerArgument("", "level")] ref ViceLoggerInterpolatedStringHandler message,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        if (!message.IsEnabled)
        {
            return;
        }

        Log(level, message.ToStringAndClear(), null, caller, file, line);
    }
}
