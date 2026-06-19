using System.Text.Json.Serialization;

namespace Vice.Jobs;

[JsonSerializable(typeof(JobDescriptor))]
internal sealed partial class JobJsonContext : JsonSerializerContext
{
}
