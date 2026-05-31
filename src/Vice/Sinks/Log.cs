using System.Runtime.CompilerServices;
using Vice.Configuration;
using Vice.Logging;
using Vice.Persistence;

namespace Vice;

public static class Log
{
    private static ILogSink _sink = NullLogSink.Instance;
    private static ILogSink _audit = FileLogSink.Default;

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

    private sealed class FileLogSink : ILogSink
    {
        public static readonly FileLogSink Default = new(ViceLogLevel.Warn);

        private readonly ViceLogLevel _minLevel;
        private readonly object _writeLock = new();
        private string? _path;
        private bool _resolved;

        private FileLogSink(ViceLogLevel minLevel)
        {
            _minLevel = minLevel;
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
            }

            Append(text);
        }

        private void Append(string line)
        {
            lock (_writeLock)
            {
                var path = ResolvePath();
                if (path is null)
                {
                    return;
                }

                try
                {
                    var record = $"{DateTime.UtcNow:o} {line}\n";
                    var created = !File.Exists(path);
                    File.AppendAllText(path, record);
                    if (created)
                    {
                        FileAccessControl.RestrictToCurrentUser(path);
                    }
                }
                catch (Exception ex) when (ex is IOException
                    || ex is UnauthorizedAccessException
                    || ex is NotSupportedException)
                {
                }
            }
        }

        private string? ResolvePath()
        {
            if (_resolved)
            {
                return _path;
            }

            _resolved = true;
            try
            {
                var dirs = new ViceDirectories("vice");
                Directory.CreateDirectory(dirs.StateDir);
                _path = Path.Combine(dirs.StateDir, "audit.log");
            }
            catch (Exception ex) when (ex is IOException
                || ex is UnauthorizedAccessException
                || ex is ArgumentException)
            {
                _path = null;
            }

            return _path;
        }
    }
}
