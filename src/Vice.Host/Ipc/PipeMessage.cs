using System.Text.Json.Serialization;

namespace Vice.Ipc;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CommandMessage), "command")]
[JsonDerivedType(typeof(HealthRequest), "healthRequest")]
[JsonDerivedType(typeof(HealthResponse), "healthResponse")]
[JsonDerivedType(typeof(CommandResponse), "commandResponse")]
internal abstract class PipeMessage { }
