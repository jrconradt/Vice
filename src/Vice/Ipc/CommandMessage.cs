namespace Vice.Ipc;

internal sealed class CommandMessage : PipeMessage
{
    public required string CommandLine { get; init; }
}
