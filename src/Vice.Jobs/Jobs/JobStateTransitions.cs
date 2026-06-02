namespace Vice.Jobs;

internal static class JobStateTransitions
{
    public static bool IsValid(JobStatus current, JobStatus target) => (current, target) switch
    {
        (JobStatus.Queued, JobStatus.Running) => true,
        (JobStatus.Queued, JobStatus.Failed) => true,
        (JobStatus.Running, JobStatus.Paused) => true,
        (JobStatus.Running, JobStatus.Completed) => true,
        (JobStatus.Running, JobStatus.Failed) => true,
        (JobStatus.Paused, JobStatus.Running) => true,
        (JobStatus.Paused, JobStatus.Failed) => true,
        _ => false
    };
}
