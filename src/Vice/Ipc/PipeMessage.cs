using System.Text.Json.Serialization;

namespace Vice.Ipc;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CommandMessage), "command")]
[JsonDerivedType(typeof(JobStatusRequest), "jobStatusRequest")]
[JsonDerivedType(typeof(JobStatusResponse), "jobStatusResponse")]
[JsonDerivedType(typeof(JobEvent), "jobEvent")]
[JsonDerivedType(typeof(CommandResponse), "commandResponse")]
internal abstract class PipeMessage { }
