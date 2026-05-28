using System.Text.Json.Serialization;

namespace Vice.Configuration;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class KeyringJsonContext : JsonSerializerContext { }
