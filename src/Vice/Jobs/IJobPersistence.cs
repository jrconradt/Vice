namespace Vice.Jobs;

internal interface IJobPersistence
{
    Task<List<JobState>> LoadAsync(CancellationToken ct);
    Task SaveAsync(IReadOnlyList<JobState> jobs, CancellationToken ct);
}
