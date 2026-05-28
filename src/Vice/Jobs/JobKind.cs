using System.Text.Json.Serialization;

namespace Vice.Jobs;

[JsonConverter(typeof(JsonStringEnumConverter<JobKind>))]
public enum JobKind
{
    Download,
    GrpcStream
}
