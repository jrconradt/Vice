using System.Runtime.CompilerServices;
using Vice.Logging;

namespace Vice;

public interface ILogSink
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
