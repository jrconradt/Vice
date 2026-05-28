using System.Text.Json.Serialization;

namespace Vice.Configuration;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SourceList))]
internal partial class SourcesJsonContext : JsonSerializerContext { }
