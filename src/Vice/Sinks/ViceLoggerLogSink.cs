using System.Runtime.CompilerServices;
using Vice.Logging;

namespace Vice;

internal sealed class ViceLoggerLogSink : ILogSink
{
    private readonly IViceLogger _logger;

    public ViceLoggerLogSink(IViceLogger logger)
    {
        _logger = logger;
    }

    public bool IsEnabled(ViceLogLevel level) => _logger.IsEnabled(level);

    public void Log(ViceError error) => _logger.Log(error);

    public void Log(
        ViceLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
        => _logger.Log(level, message, exception, caller, file, line);
}
