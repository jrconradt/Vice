using System.Text.Json.Serialization;

namespace Vice.Ipc;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PipeMessage))]
internal partial class PipeMessageJsonContext : JsonSerializerContext { }
