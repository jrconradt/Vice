using System.Text.Json.Serialization;

namespace Vice.Configuration;

public sealed record SourceList
{
    [JsonPropertyName("schema-version")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("source-list")]
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();
}
