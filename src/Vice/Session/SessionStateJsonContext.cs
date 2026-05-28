using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vice.Session;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(string))]
internal partial class SessionStateJsonContext : JsonSerializerContext { }
