using Vice.Display.Rendering;
using Vice.Logging;
using Vice.Net.Requests.Grpc;
using Xunit;

namespace Vice.Net.Tests;

public class GrpcConnectionManagerTests
{
    private const string Endpoint = "127.0.0.1:50111";

    private sealed class RecordingWriter : IConsoleWriter
    {
        public string Errors { get; private set; } = "";

        public void Write(string text)
        {
        }

        public void WriteLine(string text)
        {
        }

        public void WriteLine()
        {
        }

        public void WriteError(string text)
        {
            Errors += $"{text}\n";
        }
    }

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

    [Fact]
    public async Task Connect_default_to_non_443_port_does_not_warn_about_cleartext()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        var writer = new RecordingWriter();

        conn.Connect("127.0.0.1:8080", plaintext: false, writer);

        Assert.Equal("", writer.Errors);
    }

    [Fact]
    public async Task Connect_with_plaintext_warns_that_traffic_is_unencrypted()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        var writer = new RecordingWriter();

        conn.Connect("127.0.0.1:8080", plaintext: true, writer);

        Assert.Contains("plaintext transport", writer.Errors);
        Assert.Contains("unencrypted", writer.Errors);
    }

    [Fact]
    public async Task Connect_with_plaintext_warns_exactly_once_per_endpoint()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        var writer = new RecordingWriter();

        conn.Connect("127.0.0.1:8080", plaintext: true, writer);
        conn.Connect("127.0.0.1:8080", plaintext: true, writer);

        var occurrences = writer.Errors.Split("plaintext transport").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public async Task LeaseChannel_on_unconnected_endpoint_throws_InvalidOperationException()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        var ex = Assert.Throws<InvalidOperationException>(() => conn.LeaseChannel(Endpoint));
        Assert.Contains("Not connected", ex.Message);
    }

    [Fact]
    public async Task Disconnect_with_active_lease_keeps_the_channel_usable_until_release()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        conn.Connect(Endpoint, plaintext: true);
        using var lease = conn.LeaseChannel(Endpoint);

        Assert.True(conn.Disconnect(Endpoint));

        lease.Channel.CreateCallInvoker();
    }

    [Fact]
    public async Task Disconnect_with_active_lease_disposes_the_channel_after_last_release()
    {
        var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        conn.Connect(Endpoint, plaintext: true);
        var lease = conn.LeaseChannel(Endpoint);
        var channel = lease.Channel;

        Assert.True(conn.Disconnect(Endpoint));

        lease.Dispose();
        await conn.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => channel.CreateCallInvoker());
    }

    [Fact]
    public async Task Lease_dispose_is_idempotent_and_does_not_release_other_leases()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        conn.Connect(Endpoint, plaintext: true);
        var first = conn.LeaseChannel(Endpoint);
        using var second = conn.LeaseChannel(Endpoint);

        first.Dispose();
        first.Dispose();

        Assert.True(conn.Disconnect(Endpoint));

        second.Channel.CreateCallInvoker();
    }

    [Fact]
    public async Task LeaseChannel_after_disconnect_throws_InvalidOperationException()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        conn.Connect(Endpoint, plaintext: true);
        using var lease = conn.LeaseChannel(Endpoint);
        conn.Disconnect(Endpoint);

        var ex = Assert.Throws<InvalidOperationException>(() => conn.LeaseChannel(Endpoint));
        Assert.Contains("Not connected", ex.Message);
    }

    [Fact]
    public async Task LRU_eviction_never_retires_endpoints_with_active_leases()
    {
        const int CONNECTION_COUNT = 257;
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        var leases = new List<GrpcConnectionManager.ConnectionLease>();
        for (var i = 0; i < CONNECTION_COUNT; i++)
        {
            var endpoint = $"127.0.0.1:{10000 + i}";
            conn.Connect(endpoint, plaintext: true);
            leases.Add(conn.LeaseChannel(endpoint));
        }

        foreach (var lease in leases)
        {
            lease.Channel.CreateCallInvoker();
            lease.Dispose();
        }
    }

    [Fact]
    public async Task GetConnections_excludes_disconnected_endpoints_with_active_leases()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        conn.Connect(Endpoint, plaintext: true);
        using var lease = conn.LeaseChannel(Endpoint);
        conn.Disconnect(Endpoint);

        Assert.Empty(conn.GetConnections());
    }
}
