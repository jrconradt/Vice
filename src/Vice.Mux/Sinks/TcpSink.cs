using System.Net.Sockets;

namespace Vice.Mux.Sinks;

internal sealed class TcpSink : SinkBase
{
    private readonly TcpClient _client;

    public TcpSink(TcpClient client, string label) : base(client.GetStream(), label)
    {
        _client = client;
    }

    protected override ValueTask DisposeCoreAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
