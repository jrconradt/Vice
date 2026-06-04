using System.Text.RegularExpressions;

namespace Vice.Session;

internal sealed class InputHistory : IInputHistory
{
    private const int MAX_ENTRIES = 1000;

    private static readonly TimeSpan RedactTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex SecretFlag = new(
        @"(--(?:token|password|secret|api[-_]?key|auth(?:orization)?|bearer|metadata|header|cookie|credentials)|-H)(=(?:""[^""]*""|'[^']*'|\S+)|\s+(?:""[^""]*""|'[^']*'|\S+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RedactTimeout);

    private static readonly Regex JsonCredentialField = new(
        @"""(authorization|auth[-_]?token|api[-_]?key|x-api[-_]?key|x-auth[-_]?token|bearer|password|secret|cookie|set-cookie|token)""\s*:\s*""((?:[^""\\]|\\.)*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RedactTimeout);

    private static readonly Regex WithDataPayload = new(
        @"(\bwith\s+data\s+)(""[^""]*""|'[^']*'|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RedactTimeout);

    private static readonly Regex SendPayload = new(
        @"(\b(?:tcp|udp)\s+send\s+)(""[^""]*""|'[^']*'|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RedactTimeout);

    private readonly string[] _entries = new string[MAX_ENTRIES];

    private int _head;

    private int _count;

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
            var step2 = JsonCredentialField.Replace(step1, m => $"\"{m.Groups[1].Value}\":\"<redacted>\"");
            var step3 = WithDataPayload.Replace(step2, m => $"{m.Groups[1].Value}<redacted>");
            return SendPayload.Replace(step3, m => $"{m.Groups[1].Value}<redacted>");
        }
        catch (RegexMatchTimeoutException)
        {
            return "<redacted>";
        }
    }

    public Task AppendAsync(string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Task.CompletedTask;
        }

        command = Redact(command);

        if (_count > 0)
        {
            var lastIndex = (_head + _count - 1) % MAX_ENTRIES;
            if (_entries[lastIndex] == command)
            {
                return Task.CompletedTask;
            }
        }

        if (_count < MAX_ENTRIES)
        {
            var tail = (_head + _count) % MAX_ENTRIES;
            _entries[tail] = command;
            _count++;
        }
        else
        {
            _entries[_head] = command;
            _head = (_head + 1) % MAX_ENTRIES;
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<string> GetHistory()
    {
        var snapshot = new string[_count];
        for (var i = 0; i < _count; i++)
        {
            snapshot[i] = _entries[(_head + i) % MAX_ENTRIES];
        }

        return snapshot;
    }

    public IReadOnlyList<string> GetHistory(int count)
    {
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        if (count >= _count)
        {
            return GetHistory();
        }

        var snapshot = new string[count];
        var start = _count - count;
        for (var i = 0; i < count; i++)
        {
            snapshot[i] = _entries[(_head + start + i) % MAX_ENTRIES];
        }

        return snapshot;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
