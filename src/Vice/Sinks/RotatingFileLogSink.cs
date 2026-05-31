using System.Runtime.CompilerServices;
using Vice.Configuration;
using Vice.Logging;
using Vice.Persistence;

namespace Vice;

public sealed class RotatingFileLogSink : ILogSink
{
    private readonly ViceLogLevel _minLevel;
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _retained;
    private readonly object _writeLock = new();

    public RotatingFileLogSink(
        string path,
        ViceLogLevel minLevel,
        long maxBytes = 4L * 1024 * 1024,
        int retained = 5)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _minLevel = minLevel;
        _maxBytes = maxBytes < 1 ? 1 : maxBytes;
        _retained = retained < 0 ? 0 : retained;
    }

    public static RotatingFileLogSink ForDaemon(string appName, ViceLogLevel minLevel)
    {
        var dirs = new ViceDirectories(appName);
        Directory.CreateDirectory(dirs.StateDir);
        var path = Path.Combine(dirs.StateDir, "daemon.log");
        return new RotatingFileLogSink(path, minLevel);
    }

    public bool IsEnabled(ViceLogLevel level) => level >= _minLevel;

    public void Log(ViceError error)
    {
        if (!IsEnabled(error.LogLevel))
        {
            return;
        }

        Append(LogFormat.Format(error));
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
        var text = $"[{level.ToString().ToUpperInvariant()}] {caller}@{fileName}:{line}: {message}";
        if (exception is not null)
        {
            text += $"\n  {exception.GetType().Name}: {exception.Message}";
            if (exception.StackTrace is { } st)
            {
                text += $"\n  {st}";
            }
        }

        Append(text);
    }

    private void Append(string line)
    {
        lock (_writeLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                RotateIfNeeded();

                var record = $"{DateTime.UtcNow:o} {line}\n";
                var created = !File.Exists(_path);
                File.AppendAllText(_path, record);
                if (created)
                {
                    FileAccessControl.RestrictToCurrentUser(_path);
                }
            }
            catch (Exception ex) when (ex is IOException
                || ex is UnauthorizedAccessException
                || ex is NotSupportedException
                || ex is ArgumentException)
            {
            }
        }
    }

    private void RotateIfNeeded()
    {
        long current;
        try
        {
            var info = new FileInfo(_path);
            current = info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException
            || ex is UnauthorizedAccessException)
        {
            return;
        }

        if (current < _maxBytes)
        {
            return;
        }

        if (_retained == 0)
        {
            TryDelete(_path);
            return;
        }

        TryDelete($"{_path}.{_retained}");
        var index = _retained - 1;
        while (index >= 1)
        {
            var source = $"{_path}.{index}";
            var dest = $"{_path}.{index + 1}";
            TryMove(source, dest);
            index--;
        }

        TryMove(_path, $"{_path}.1");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException
            || ex is UnauthorizedAccessException)
        {
        }
    }

    private static void TryMove(string source, string dest)
    {
        try
        {
            if (File.Exists(source))
            {
                File.Move(source, dest, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException
            || ex is UnauthorizedAccessException)
        {
        }
    }
}
