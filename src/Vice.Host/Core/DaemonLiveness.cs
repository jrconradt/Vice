namespace Vice.Host.Core;

internal readonly record struct DaemonLiveness(
    bool Listening,
    bool AcceptLoopCrashed,
    string? FaultSummary);
