using System.Runtime.CompilerServices;
using Vice.Logging;

namespace Vice;

public static class Log
{
    private static ILogSink _sink = NullLogSink.Instance;
    private static ILogSink _audit = NullLogSink.Instance;

    public static void Configure(ILogSink sink)
        => Volatile.Write(ref _sink, sink ?? NullLogSink.Instance);

    public static void ConfigureAudit(ILogSink sink)
        => Volatile.Write(ref _audit, sink ?? NullLogSink.Instance);

    public static ILogSink Current => Volatile.Read(ref _sink);

    public static ILogSink Audit => Volatile.Read(ref _audit);

    public static bool IsEnabled(ViceLogLevel level)
    {
        if (Volatile.Read(ref _sink).IsEnabled(level))
        {
            return true;
        }

        return Volatile.Read(ref _audit).IsEnabled(level);
    }

    public static void Emit(ViceError error)
    {
        Volatile.Read(ref _sink).Log(error);
        Volatile.Read(ref _audit).Log(error);
    }

    public static void Emit(
        ViceLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        Volatile.Read(ref _sink).Log(level, message, exception, caller, file, line);
        Volatile.Read(ref _audit).Log(level, message, exception, caller, file, line);
    }
}
