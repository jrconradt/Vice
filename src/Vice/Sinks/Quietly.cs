using System.Runtime.CompilerServices;
using Vice.Logging;

namespace Vice;

public static class Quietly
{
    public static void Swallow(
        Exception exception,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        Log.Emit(ViceLogLevel.Trace,
                 $"Swallowed {exception.GetType().Name} in {caller}.",
                 exception,
                 caller,
                 file,
                 line);
    }
}
