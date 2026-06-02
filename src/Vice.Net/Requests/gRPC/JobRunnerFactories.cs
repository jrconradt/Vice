using Vice.Composition;
using Vice.Jobs;

namespace Vice.Net.Requests.Grpc;

public static class JobFactory
{
    [ViceJobRunner]
    public static IJobRunner GrpcStream(GrpcConnectionManager grpc) => new GrpcStreamJobRunner(grpc);
}
