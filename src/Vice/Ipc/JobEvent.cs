namespace Vice.Ipc;

internal sealed class JobEvent : PipeMessage
{
    public required int JobId { get; init; }
    public required string EventType { get; init; }
    public string? Details { get; init; }
}
