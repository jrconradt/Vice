using Vice.Build.Dotnet;
using Vice.Composition;
using Vice.Mux.Sinks;
using Vice.Net.Requests.Grpc;

namespace Vice.Cli;

[ViceHost]
internal sealed class ViceHostServices
{
    [ViceSessionService]
    public required GrpcConnectionManager Grpc { get; init; }

    [ViceSessionService]
    public required DotnetBuildQueue Build { get; init; }

    [ViceSessionService]
    public required TcpSinkConnector ConnectTcpSink { get; init; }
}
