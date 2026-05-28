using System.Text.Json.Serialization;

namespace Vice.Logging;

[JsonSerializable(typeof(Dictionary<string, string?>))]
internal partial class TelemetryJsonContext : JsonSerializerContext { }
