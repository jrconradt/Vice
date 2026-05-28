using Vice.Composition;
using Vice.Jobs;

namespace Vice.Network.gRPC;

internal static class JobFactory
{
    [ViceJobRunner]
    public static IJobRunner GrpcStream(GrpcConnectionManager grpc) => new GrpcStreamJobRunner(grpc);
}
