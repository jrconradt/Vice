using System.Text.Json.Serialization;

namespace Vice.Research;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(List<SearchHit>))]
[JsonSerializable(typeof(SearchHit))]
internal partial class ResearchJsonContext : JsonSerializerContext { }
