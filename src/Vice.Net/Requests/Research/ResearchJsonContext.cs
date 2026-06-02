using System.Text.Json.Serialization;

namespace Vice.Net.Research;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(List<SearchHit>))]
[JsonSerializable(typeof(SearchHit))]
internal partial class ResearchJsonContext : JsonSerializerContext { }
