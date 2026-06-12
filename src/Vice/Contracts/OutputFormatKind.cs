namespace Vice.Contracts;

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

        switch (normalized.ToLowerInvariant())
        {
            case "auto":
                {
                    kind = OutputFormatKind.Auto;
                    return true;
                }
            case "text":
                {
                    kind = OutputFormatKind.Text;
                    return true;
                }
            case "hex":
                {
                    kind = OutputFormatKind.Hex;
                    return true;
                }
            case "json":
                {
                    kind = OutputFormatKind.Json;
                    return true;
                }
            case "jsonl":
                {
                    kind = OutputFormatKind.Jsonl;
                    return true;
                }
            case "ndjson":
                {
                    kind = OutputFormatKind.Ndjson;
                    return true;
                }
            default:
                {
                    kind = OutputFormatKind.Auto;
                    return false;
                }
        }
    }
}
