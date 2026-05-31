using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Vice.Concurrency;
using Vice.Logging;
using Vice.Persistence;

namespace Vice.Session;

internal sealed class InputHistory : IInputHistory
{
    private const int MaxEntries = 1000;
    private const long MaxLoadBytes = 4L * 1024 * 1024;

    private static readonly TimeSpan RedactTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex SecretFlag = new(
        @"(--(?:token|password|secret|api[-_]?key|auth(?:orization)?|bearer|metadata|header|cookie|credentials)|-H)(=(?:""[^""]*""|'[^']*'|\S+)|\s+(?:""[^""]*""|'[^']*'|\S+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RedactTimeout);

    private static readonly Regex JsonCredentialField = new(
        @"""(authorization|auth[-_]?token|api[-_]?key|x-api[-_]?key|x-auth[-_]?token|bearer|password|secret|cookie|set-cookie|token)""\s*:\s*""([^""\\]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RedactTimeout);

    private readonly string _filePath;
    private readonly SerialQueue _fileQueue = new();
    private ImmutableList<string> _entries = ImmutableList<string>.Empty;

    internal static string Redact(string command)
    {
        try
        {
            var step1 = SecretFlag.Replace(command, m =>
            {
                var flag = m.Groups[1].Value;
                var sep = m.Groups[2].Value[0];
                return sep == '=' ? $"{flag}=<redacted>" : $"{flag} <redacted>";
            });
            return JsonCredentialField.Replace(step1, m => $"\"{m.Groups[1].Value}\":\"<redacted>\"");
        }
        catch (RegexMatchTimeoutException)
        {
            return "<redacted>";
        }
    }

    public InputHistory(string filePath, IViceLogger? logger = null)
    {
        _ = logger;
        _filePath = filePath;
    }

    public void Load()
    {
        var builder = ImmutableList.CreateBuilder<string>();

        if (File.Exists(_filePath))
        {
            try
            {
                foreach (var line in ReadCappedLines(_filePath))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        builder.Add(line);
                    }
                }
            }
            catch (IOException ex)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, $"history load failed at {_filePath}", ex);
            }
        }

        var loaded = builder.ToImmutable();
        if (loaded.Count > MaxEntries)
        {
            loaded = loaded.RemoveRange(0, loaded.Count - MaxEntries);
        }

        Volatile.Write(ref _entries, loaded);
    }

    private static IEnumerable<string> ReadCappedLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = fs.Length;
        var toRead = length > MaxLoadBytes ? MaxLoadBytes : length;

        if (length > MaxLoadBytes)
        {
            fs.Seek(length - MaxLoadBytes, SeekOrigin.Begin);
        }

        var buffer = new byte[toRead];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = fs.Read(buffer, offset, buffer.Length - offset);
            if (n <= 0)
            {
                break;
            }

            offset += n;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, offset);
        var split = text.Split('\n');

        if (length > MaxLoadBytes && split.Length > 0)
        {
            split = split[1..];
        }

        var result = new List<string>(split.Length);
        foreach (var s in split)
        {
            var trimmed = s.EndsWith('\r') ? s[..^1] : s;
            result.Add(trimmed);
        }
        return result;
    }

    public Task AppendAsync(string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Task.CompletedTask;
        }

        command = Redact(command);

        bool rewrite;
        ImmutableList<string> committed;
        while (true)
        {
            var current = Volatile.Read(ref _entries);
            if (current.Count > 0 && current[^1] == command)
            {
                return Task.CompletedTask;
            }

            var appended = current.Add(command);
            ImmutableList<string> next;
            if (appended.Count > MaxEntries)
            {
                next = appended.RemoveRange(0, appended.Count - MaxEntries);
                rewrite = true;
            }
            else
            {
                next = appended;
                rewrite = false;
            }

            if (Interlocked.CompareExchange(ref _entries, next, current) == current)
            {
                committed = next;
                break;
            }
        }

        if (rewrite)
        {
            var payload = string.Join(Environment.NewLine, committed) + Environment.NewLine;
            return _fileQueue.EnqueueAsync(c =>
                AtomicFile.WriteAllTextAsync(_filePath, payload, c), ct);
        }

        return _fileQueue.EnqueueAsync(c =>
            AtomicFile.AppendTextAsync(_filePath, command + Environment.NewLine, c), ct);
    }

    public IReadOnlyList<string> GetHistory()
        => Volatile.Read(ref _entries);

    public IReadOnlyList<string> GetHistory(int count)
    {
        var snapshot = Volatile.Read(ref _entries);
        if (count >= snapshot.Count)
        {
            return snapshot;
        }

        return snapshot.GetRange(snapshot.Count - count, count);
    }

    public ValueTask DisposeAsync() => _fileQueue.DisposeAsync();
}
