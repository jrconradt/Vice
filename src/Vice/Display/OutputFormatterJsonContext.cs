using System.Text.Json.Serialization;

namespace Vice.Display;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OutputFormatJsonPayload))]
internal partial class OutputFormatterJsonContext : JsonSerializerContext { }
