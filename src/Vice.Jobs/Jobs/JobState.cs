using System.Text.Json.Serialization;

namespace Vice.Jobs;

public sealed record JobState
{
    [JsonInclude]
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonInclude]
    [JsonPropertyName("kind")]
    public JobKind Kind { get; init; }

    [JsonInclude]
    [JsonPropertyName("status")]
    public JobStatus Status { get; init; } = JobStatus.Running;

    [JsonInclude]
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonInclude]
    [JsonPropertyName("progressCurrent")]
    public long ProgressCurrent { get; init; }

    [JsonInclude]
    [JsonPropertyName("progressTotal")]
    public long? ProgressTotal { get; init; }

    [JsonInclude]
    [JsonPropertyName("processStartTimeUtc")]
    public DateTime? ProcessStartTimeUtc { get; init; }

    [JsonInclude]
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonInclude]
    [JsonPropertyName("options")]
    public IReadOnlyDictionary<string, string?> Options { get; init; }
        = EmptyOptions;

    [JsonInclude]
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    [JsonInclude]
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; init; }

    [JsonInclude]
    [JsonPropertyName("lastProgressAt")]
    public DateTime? LastProgressAt { get; init; }

    [JsonInclude]
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; init; }

    private static readonly IReadOnlyDictionary<string, string?> EmptyOptions
        = new Dictionary<string, string?>(StringComparer.Ordinal);
}
