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
}
