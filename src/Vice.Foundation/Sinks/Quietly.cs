using System.Runtime.CompilerServices;
using Vice.Logging;

namespace Vice.Logging;

public static class Quietly
{
    public static void Swallow(
        Exception exception,
        IViceLogger? logger = null,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        var sink = logger ?? NullViceLogger.Instance;
        sink.Log(ViceLogLevel.Trace,
                 $"Swallowed {exception.GetType().Name} in {caller}.",
                 exception,
                 caller,
                 file,
                 line);
    }
}
