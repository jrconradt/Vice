using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Vice.Session;

internal sealed class InputHistory : IInputHistory
{
    private const int MaxEntries = 1000;

    private static readonly TimeSpan RedactTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex SecretFlag = new(
        @"(--(?:token|password|secret|api[-_]?key|auth(?:orization)?|bearer|metadata|header|cookie|credentials)|-H)(=(?:""[^""]*""|'[^']*'|\S+)|\s+(?:""[^""]*""|'[^']*'|\S+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RedactTimeout);

    private static readonly Regex JsonCredentialField = new(
        @"""(authorization|auth[-_]?token|api[-_]?key|x-api[-_]?key|x-auth[-_]?token|bearer|password|secret|cookie|set-cookie|token)""\s*:\s*""([^""\\]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RedactTimeout);

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

    public Task AppendAsync(string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Task.CompletedTask;
        }

        command = Redact(command);

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
            }
            else
            {
                next = appended;
            }

            if (Interlocked.CompareExchange(ref _entries, next, current) == current)
            {
                return Task.CompletedTask;
            }
        }
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
