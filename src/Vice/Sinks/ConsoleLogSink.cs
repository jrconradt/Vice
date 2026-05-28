using System.Runtime.CompilerServices;
using Vice.Logging;

namespace Vice;

internal sealed class ConsoleLogSink : ILogSink
{
    private readonly ViceLogLevel _minLevel;
    private readonly TextWriter _sink;
    private readonly object _writeLock = new();

    public ConsoleLogSink(ViceLogLevel minLevel, TextWriter? sink = null)
    {
        _minLevel = minLevel;
        _sink = sink ?? System.Console.Error;
    }

    public bool IsEnabled(ViceLogLevel level) => level >= _minLevel;

    public void Log(ViceError error)
    {
        if (!IsEnabled(error.LogLevel))
        {
            return;
        }

        lock (_writeLock)
        {
            _sink.WriteLine(LogFormat.Format(error));
            _sink.Flush();
        }
    }

    public void Log(
        ViceLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var fileName = file is null ? "?" : Path.GetFileName(file);
        var text = $"[{level.ToString().ToUpperInvariant()}] {caller}@{fileName}:{line}: {message}\n";
        if (exception is not null)
        {
            text += $"  {exception.GetType().Name}: {exception.Message}\n";
            if (exception.StackTrace is { } st)
            {
                text += $"  {st}\n";
            }
        }

        lock (_writeLock)
        {
            _sink.Write(text);
            _sink.Flush();
        }
    }
}
