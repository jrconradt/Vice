namespace Vice.Jobs;

public interface IJobRunner
{
    Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct);

    bool CanHandle(JobKind kind);
}
