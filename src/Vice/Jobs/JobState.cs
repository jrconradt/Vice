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
    public JobStatus Status { get; init; } = JobStatus.Queued;

    [JsonInclude]
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonInclude]
    [JsonPropertyName("resourceId")]
    public string ResourceId { get; init; } = string.Empty;

    [JsonInclude]
    [JsonPropertyName("destinationPath")]
    public string DestinationPath { get; init; } = string.Empty;

    [JsonInclude]
    [JsonPropertyName("bytesDownloaded")]
    public long BytesDownloaded { get; init; }

    [JsonInclude]
    [JsonPropertyName("totalBytes")]
    public long? TotalBytes { get; init; }

    [JsonInclude]
    [JsonPropertyName("messagesReceived")]
    public long MessagesReceived { get; init; }

    [JsonInclude]
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonInclude]
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    [JsonInclude]
    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonInclude]
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    [JsonInclude]
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; init; }
}
