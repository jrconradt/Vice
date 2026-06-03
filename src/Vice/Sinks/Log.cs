using System.Runtime.CompilerServices;
using Vice.Logging;

namespace Vice;

public static class Log
{
    private static IViceLogger _sink = NullViceLogger.Instance;

    public static void Configure(IViceLogger sink)
        => Volatile.Write(ref _sink, sink ?? NullViceLogger.Instance);

    public static IViceLogger Current => Volatile.Read(ref _sink);

    public static bool IsEnabled(ViceLogLevel level)
        => Volatile.Read(ref _sink).IsEnabled(level);

    public static void Emit(ViceError error)
    {
        Volatile.Read(ref _sink).Log(error);
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
    }

    public static void Emit(
        ViceLogLevel level,
        [InterpolatedStringHandlerArgument("level")] ref LogInterpolatedStringHandler message,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        if (!message.IsEnabled)
        {
            return;
        }

        Volatile.Read(ref _sink).Log(level, message.ToStringAndClear(), null, caller, file, line);
    }
}
