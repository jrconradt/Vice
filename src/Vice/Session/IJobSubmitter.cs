using Vice.Jobs;

namespace Vice.Session;

public interface IJobSubmitter
{
    Task<int> SubmitAsync(JobDescriptor descriptor, CancellationToken ct);
}
