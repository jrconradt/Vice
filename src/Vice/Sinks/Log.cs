using System.Runtime.CompilerServices;
using Vice.Logging;

namespace Vice;

public static class Log
{
    private static ILogSink _sink = NullLogSink.Instance;

    public static void Configure(ILogSink sink)
        => Volatile.Write(ref _sink, sink ?? NullLogSink.Instance);

    public static ILogSink Current => Volatile.Read(ref _sink);

    public static bool IsEnabled(ViceLogLevel level)
        => Volatile.Read(ref _sink).IsEnabled(level);

    public static void Emit(ViceError error)
        => Volatile.Read(ref _sink).Log(error);

    public static void Emit(
        ViceLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
        => Volatile.Read(ref _sink).Log(level, message, exception, caller, file, line);
}
