using Vice.Build.Dotnet;
using Vice.Composition;
using Vice.Net.Requests.Grpc;

namespace Vice.Cli;

[ViceHost]
internal sealed class ViceHostServices
{
    [ViceSessionService]
    public required GrpcConnectionManager Grpc { get; init; }

    [ViceSessionService]
    public required DotnetBuildQueue Build { get; init; }
}
