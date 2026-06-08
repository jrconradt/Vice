namespace Vice.Jobs;

public interface IJobManager : IJobSubmitter, IAsyncDisposable
{
    event Action<JobState>? JobCompleted;
    event Action<JobState, string>? JobFailed;

    Task PauseAsync(int jobId, CancellationToken ct);
    Task ResumeAsync(int jobId, CancellationToken ct);
    Task CancelAsync(int jobId, CancellationToken ct);
    IReadOnlyList<JobState> GetJobs();
    JobState? GetJob(int id);
    WorkerPoolHealth GetWorkerPoolHealth();
}
