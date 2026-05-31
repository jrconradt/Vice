using System.Collections.Concurrent;
using Vice.Jobs;

namespace Vice.Session;

internal readonly record struct JobView(int Id, JobKind Kind, JobStatus Status, string Label, string Progress)
{
    public static JobView From(JobState job)
    {
        var formatter = JobViewFormatters.Resolve(job.Kind);
        return new JobView(job.Id,
                           job.Kind,
                           job.Status,
                           formatter.Label(job),
                           formatter.Progress(job));
    }
}

public sealed record JobViewFormatter(Func<JobState, string> Label, Func<JobState, string> Progress);

public static class JobViewFormatters
{
    private static readonly ConcurrentDictionary<JobKind, JobViewFormatter> _formatters = new();

    private static readonly JobViewFormatter _fallback = new(FallbackLabel, FallbackProgress);

    static JobViewFormatters()
    {
        Register(JobKind.Download,
                 new JobViewFormatter(static job => $"{job.Source}/{job.ResourceId}",
                                      DownloadProgress));
        Register(JobKind.GrpcStream,
                 new JobViewFormatter(static job => $"{job.Method} on {job.Endpoint}",
                                      static job => $"{job.MessagesReceived} msgs"));
    }

    public static void Register(JobKind kind, JobViewFormatter formatter)
    {
        _formatters[kind] = formatter;
    }

    public static JobViewFormatter Resolve(JobKind kind)
    {
        return _formatters.TryGetValue(kind, out var formatter)
            ? formatter
            : _fallback;
    }

    private static string DownloadProgress(JobState job)
    {
        return job.TotalBytes > 0
            ? $"{job.BytesDownloaded * 100 / job.TotalBytes.Value}%"
            : "";
    }

    private static string FallbackLabel(JobState job)
    {
        if (!string.IsNullOrEmpty(job.Endpoint) && !string.IsNullOrEmpty(job.Method))
        {
            return $"{job.Method} on {job.Endpoint}";
        }

        if (!string.IsNullOrEmpty(job.Source) || !string.IsNullOrEmpty(job.ResourceId))
        {
            return $"{job.Source}/{job.ResourceId}";
        }

        return job.Kind.Name;
    }

    private static string FallbackProgress(JobState job)
    {
        if (job.TotalBytes > 0)
        {
            return $"{job.BytesDownloaded * 100 / job.TotalBytes.Value}%";
        }

        if (job.MessagesReceived > 0)
        {
            return $"{job.MessagesReceived} msgs";
        }

        return "";
    }
}
