namespace Vice.Jobs;

public interface IJobRunner
{
    Task RunAsync(JobDescriptor descriptor, CancellationToken ct);

    bool CanHandle(JobKind kind);
}
