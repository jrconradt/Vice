using System.Text.Json.Serialization;

namespace Vice.Jobs;

[JsonConverter(typeof(JsonStringEnumConverter<JobStatus>))]
public enum JobStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed
}
