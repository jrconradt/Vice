namespace Vice.Ipc;

internal sealed class JobStatusResponse : PipeMessage
{
    public required List<JobStatusEntry> Jobs { get; init; }
}
