using System.Runtime.CompilerServices;
using Vice.Logging;

namespace Vice;

internal sealed class NullLogSink : ILogSink
{
    public static readonly NullLogSink Instance = new();

    private NullLogSink() { }

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
