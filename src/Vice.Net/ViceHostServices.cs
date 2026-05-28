using Vice.Composition;
using Vice.Network.gRPC;

namespace Vice.Net;

[ViceHost]
internal sealed class ViceHostServices
{
    [ViceSessionService]
    public required GrpcConnectionManager Grpc { get; init; }
}
