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
    public async Task GetChannel_returns_the_channel_the_owner_connected()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        var connected = conn.Connect(Endpoint, plaintext: true);
        var borrowed = conn.GetChannel(Endpoint);

        Assert.Same(connected, borrowed);
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
    public async Task Disconnect_disposes_the_channel()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        var channel = conn.Connect(Endpoint, plaintext: true);
        conn.Disconnect(Endpoint);

        Assert.Throws<ObjectDisposedException>(() => channel.CreateCallInvoker());
    }

    [Fact]
    public async Task GetChannel_after_disconnect_throws_InvalidOperationException()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        conn.Connect(Endpoint, plaintext: true);
        conn.Disconnect(Endpoint);

        var ex = Assert.Throws<InvalidOperationException>(() => conn.GetChannel(Endpoint));
        Assert.Contains("Not connected", ex.Message);
    }

    [Fact]
    public async Task Connect_after_disconnect_creates_a_new_channel()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        var first = conn.Connect(Endpoint, plaintext: true);
        conn.Disconnect(Endpoint);
        var second = conn.Connect(Endpoint, plaintext: true);

        Assert.NotSame(first, second);
        second.CreateCallInvoker();
    }

    [Fact]
    public async Task GetConnections_reflects_connected_endpoints()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        Assert.Empty(conn.GetConnections());

        conn.Connect(Endpoint, plaintext: true);

        var connections = conn.GetConnections();
        var info = Assert.Single(connections);
        Assert.Equal(Endpoint, info.Endpoint);
    }

    [Fact]
    public async Task GetConnections_excludes_disconnected_endpoints()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        conn.Connect(Endpoint, plaintext: true);
        conn.Disconnect(Endpoint);

        Assert.Empty(conn.GetConnections());
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
    public async Task Shutdown_disposes_every_connected_channel()
    {
        var conn = new GrpcConnectionManager(NullViceLogger.Instance);

        var first = conn.Connect("127.0.0.1:50111", plaintext: true);
        var second = conn.Connect("127.0.0.1:50112", plaintext: true);

        await conn.ShutdownAsync(TimeSpan.FromSeconds(1));

        Assert.Throws<ObjectDisposedException>(() => first.CreateCallInvoker());
        Assert.Throws<ObjectDisposedException>(() => second.CreateCallInvoker());
        Assert.Empty(conn.GetConnections());

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
}
