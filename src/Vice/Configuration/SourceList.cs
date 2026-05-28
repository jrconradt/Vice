using System.Text.Json.Serialization;

namespace Vice.Configuration;

public sealed record SourceList
{
    [JsonPropertyName("source-list")]
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();
}
