using Vice.Logging;
using Vice.Network.gRPC;
using Xunit;

namespace Vice.Net.Tests;

public class GrpcConnectionManagerTests
{
    private const string Endpoint = "127.0.0.1:50111";

    [Fact]
    public async Task Connect_twice_for_one_endpoint_returns_same_channel_instance()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        var first = conn.Connect(Endpoint, plaintext: true);
        var second = conn.Connect(Endpoint, plaintext: true);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetChannel_on_unconnected_endpoint_throws_InvalidOperationException()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        var ex = Assert.Throws<InvalidOperationException>(() => conn.GetChannel(Endpoint));
        Assert.Contains("Not connected", ex.Message);
    }

    [Fact]
    public async Task Disconnect_returns_false_for_unknown_and_true_after_connect()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        Assert.False(conn.Disconnect(Endpoint));

        conn.Connect(Endpoint, plaintext: true);
        Assert.True(conn.Disconnect(Endpoint));
        Assert.False(conn.Disconnect(Endpoint));
    }

    [Fact]
    public async Task GetConnections_reflects_connected_endpoints_and_call_count()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        Assert.Empty(conn.GetConnections());

        conn.Connect(Endpoint, plaintext: true);
        conn.GetChannel(Endpoint);

        var connections = conn.GetConnections();
        var info = Assert.Single(connections);
        Assert.Equal(Endpoint, info.Endpoint);
        Assert.Equal(1, info.CallCount);
    }

    [Fact]
    public async Task Connect_with_mismatched_security_flags_throws_InvalidOperationException()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        conn.Connect(Endpoint, plaintext: true);

        Assert.Throws<InvalidOperationException>(() => conn.Connect(Endpoint, plaintext: false));
    }

    [Fact]
    public async Task Connect_after_shutdown_throws_InvalidOperationException()
    {
        var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        await conn.ShutdownAsync(TimeSpan.FromSeconds(1));

        var ex = Assert.Throws<InvalidOperationException>(() => conn.Connect(Endpoint, plaintext: true));
        Assert.Contains("shutting down", ex.Message);

        await conn.DisposeAsync();
    }
}
