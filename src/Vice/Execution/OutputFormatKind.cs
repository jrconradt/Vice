using System.Collections.Concurrent;

namespace Vice.Execution;

public enum OutputFormatKind
{
    Auto,
    Text,
    Hex,
    Json,
    Jsonl,
    Ndjson,
}

public static class OutputFormatKindParser
{
    private static readonly ConcurrentDictionary<string, OutputFormatKind> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    static OutputFormatKindParser()
    {
        Register("auto", OutputFormatKind.Auto);
        Register("text", OutputFormatKind.Text);
        Register("hex", OutputFormatKind.Hex);
        Register("json", OutputFormatKind.Json);
        Register("jsonl", OutputFormatKind.Jsonl);
        Register("ndjson", OutputFormatKind.Ndjson);
    }

    public static void Register(string name, OutputFormatKind kind)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Format name must be non-empty.", nameof(name));
        }

        _byName[name.Trim()] = kind;
    }

    public static OutputFormatKind Parse(string? raw)
    {
        return TryParse(raw, out var kind) ? kind : OutputFormatKind.Auto;
    }

    public static bool TryParse(string? raw, out OutputFormatKind kind)
    {
        var normalized = raw?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            kind = OutputFormatKind.Auto;
            return true;
        }

        return _byName.TryGetValue(normalized, out kind);
    }
}
