namespace Vice.Ipc;

internal sealed class CommandResponse : PipeMessage
{
    public required int ExitCode { get; init; }
    public required string Output { get; init; }
    public string? Error { get; init; }
}
