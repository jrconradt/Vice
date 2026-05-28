using Vice.Jobs;

namespace Vice.Session;

internal readonly record struct JobView(int Id, JobKind Kind, JobStatus Status, string Label, string Progress)
{
    public static JobView From(JobState job)
    {
        var label = job.Kind switch
        {
            JobKind.Download => $"{job.Source}/{job.ResourceId}",
            _ => $"{job.Method} on {job.Endpoint}",
        };
        var progress = (job.Kind == JobKind.Download && job.TotalBytes > 0)
            ? $"{job.BytesDownloaded * 100 / job.TotalBytes.Value}%"
            : job.Kind != JobKind.Download
                ? $"{job.MessagesReceived} msgs"
                : "";
        return new JobView(job.Id, job.Kind, job.Status, label, progress);
    }
}
