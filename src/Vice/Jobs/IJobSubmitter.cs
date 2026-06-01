namespace Vice.Jobs;

public interface IJobSubmitter
{
    Task<int> SubmitAsync(JobDescriptor descriptor, CancellationToken ct);
}
