using System.Runtime.CompilerServices;
using Vice.Concurrency;
using Vice.Logging;

namespace Vice;

internal sealed class ConsoleLogSink : ILogSink, IAsyncDisposable
{
    private static readonly string[] LEVEL_LABELS =
    {
        "TRACE",
        "DEBUG",
        "INFO",
        "WARN",
        "ERROR",
    };

    private readonly ViceLogLevel _minLevel;
    private readonly TextWriter _sink;
    private readonly SerialQueue _queue;

    public ConsoleLogSink(ViceLogLevel minLevel, TextWriter? sink = null)
    {
        _minLevel = minLevel;
        _sink = sink ?? System.Console.Error;
        _queue = new SerialQueue();
    }

    public bool IsEnabled(ViceLogLevel level) => level >= _minLevel;

    public void Log(ViceError error)
    {
        if (!IsEnabled(error.LogLevel))
        {
            return;
        }

        Append($"{LogFormat.Format(error)}\n");
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
        var text = $"[{LEVEL_LABELS[(int)level]}] {caller}@{fileName}:{line}: {message}\n";
        if (exception is not null)
        {
            text += $"  {exception.GetType().Name}: {exception.Message}\n";
            if (exception.StackTrace is { } st)
            {
                text += $"  {st}\n";
            }
        }

        Append(text);
    }

    private void Append(string text)
    {
        _ = _queue.EnqueueAsync(async ct =>
        {
            await _sink.WriteAsync(text.AsMemory(), ct).ConfigureAwait(false);
            await _sink.FlushAsync(ct).ConfigureAwait(false);
        });
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();
}
