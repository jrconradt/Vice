using System.Runtime.CompilerServices;

namespace Vice.Logging;

public sealed class ConsoleViceLogger : IViceLogger
{
    private readonly ILogSink _sink;

    public ConsoleViceLogger(ViceLogLevel minLevel, TextWriter? sink = null)
    {
        _sink = new ConsoleLogSink(minLevel, sink);
    }

    public bool IsEnabled(ViceLogLevel level) => _sink.IsEnabled(level);

    public void Log(ViceError error) => _sink.Log(error);

    public void Log(
        ViceLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
        => _sink.Log(level, message, exception, caller, file, line);
}
