using System.Text.Json.Serialization;

namespace Vice.Jobs;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<JobState>))]
internal partial class JobPersistenceJsonContext : JsonSerializerContext { }
