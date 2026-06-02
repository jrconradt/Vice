using System.Net.Sockets;
using Vice.Logging;

namespace Vice.Mux.Sinks;

internal sealed class TcpSink : StreamBackedSink
{
    private readonly TcpClient _client;

    public TcpSink(TcpClient client, string label, IViceLogger logger)
        : base(client.GetStream(), label, logger)
    {
        _client = client;
    }

    protected override ValueTask DisposeUnderlyingAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
