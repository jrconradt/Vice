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
    public static OutputFormatKind Parse(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null or "" or "auto" => OutputFormatKind.Auto,
        "text" => OutputFormatKind.Text,
        "hex" => OutputFormatKind.Hex,
        "json" => OutputFormatKind.Json,
        "jsonl" => OutputFormatKind.Jsonl,
        "ndjson" => OutputFormatKind.Ndjson,
        _ => OutputFormatKind.Auto,
    };

    public static bool TryParse(string? raw, out OutputFormatKind kind)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case null or "" or "auto":
                kind = OutputFormatKind.Auto;
                return true;
            case "text":
                kind = OutputFormatKind.Text;
                return true;
            case "hex":
                kind = OutputFormatKind.Hex;
                return true;
            case "json":
                kind = OutputFormatKind.Json;
                return true;
            case "jsonl":
                kind = OutputFormatKind.Jsonl;
                return true;
            case "ndjson":
                kind = OutputFormatKind.Ndjson;
                return true;
            default:
                kind = OutputFormatKind.Auto;
                return false;
        }
    }
}
