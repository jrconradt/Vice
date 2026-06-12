namespace Vice.Ipc;

internal sealed class HealthResponse : PipeMessage
{
    public required string Version { get; init; }

    public required bool Listening { get; init; }

    public required bool AcceptLoopCrashed { get; init; }

    public required string? FaultSummary { get; init; }

    public required double UptimeSeconds { get; init; }

    public required int JobCount { get; init; }
}
