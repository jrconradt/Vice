using System.Net.Sockets;

namespace Vice.Logging;

public sealed class SocketFailure(string protocol, SocketException inner) : ViceError(inner, protocol, inner.Message)
{
    public string Protocol { get; } = protocol;
    public SocketException Inner => (SocketException)InnerException!;
    public override ViceLogLevel LogLevel => ViceLogLevel.Warn;
    public override string? Hint => "Verify the host and port are reachable, and that no firewall blocks the connection.";
    public override string ToString() => $"{Protocol} error: {Inner.Message}";
}
