using Vice.Composition;
using Vice.Mux.Sinks;

namespace Vice.Mux.Cli;

[ViceHost]
internal sealed class ViceMuxHost
{
    [ViceSessionService]
    public required TcpSinkConnector ConnectTcpSink { get; init; }
}
