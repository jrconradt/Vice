
namespace Vice.Jobs;

internal interface IJobManager : IJobSubmitter, IAsyncDisposable
{
    Task PauseAsync(int jobId, CancellationToken ct);
    Task ResumeAsync(int jobId, CancellationToken ct);
    Task CancelAsync(int jobId, CancellationToken ct);
    IReadOnlyList<JobState> GetJobs();
    JobState? GetJob(int id);
}
