using System.Collections.Concurrent;
using Grpc.Net.Client;
using Vice.Display.Rendering;
using Vice.Logging;

namespace Vice.Net.Requests.Grpc;

public sealed class GrpcConnectionManager : IAsyncDisposable
{
    private const int MAX_GRPC_MESSAGE_BYTES = 32 * 1024 * 1024;
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Lazy<ConnectionEntry>> _connections = new();
    private readonly IViceLogger _logger;
    private volatile bool _shuttingDown;

    public GrpcConnectionManager(IViceLogger? logger = null)
    {
        _logger = logger ?? NullViceLogger.Instance;
    }

    private static void WarnPlaintext(IViceLogger logger, string endpoint, IConsoleWriter? writer)
    {
        logger.Log(
            ViceLogLevel.Warn,
            $"plaintext gRPC transport for {endpoint} via --plaintext flag; request bodies and metadata (auth tokens) are sent unencrypted");

        (writer ?? Vice.Display.NullConsoleWriter.Instance).WriteError(
            $"vice: WARNING: plaintext transport for endpoint {endpoint}; request bodies and metadata (auth tokens) are sent unencrypted");
    }

    public GrpcChannel Connect(string endpoint, bool plaintext = false, IConsoleWriter? writer = null)
    {
        if (_shuttingDown)
        {
            throw new InvalidOperationException(
                $"GrpcConnectionManager is shutting down; cannot connect to {endpoint}.");
        }

        var lazy = _connections.GetOrAdd(
            endpoint,
            ep => new Lazy<ConnectionEntry>(
                () => CreateEntry(ep, plaintext, writer),
                LazyThreadSafetyMode.ExecutionAndPublication));

        var entry = lazy.Value;
        if (entry.Plaintext != plaintext)
        {
            throw new InvalidOperationException(
                $"Cached connection to {endpoint} was established with plaintext={entry.Plaintext}; refusing to reuse it for a request with plaintext={plaintext}.");
        }

        return entry.Channel;
    }

    public GrpcChannel GetChannel(string endpoint)
    {
        if (!_connections.TryGetValue(endpoint, out var lazy))
        {
            throw new InvalidOperationException($"Not connected to {endpoint}");
        }

        return lazy.Value.Channel;
    }

    public bool Disconnect(string endpoint)
    {
        if (!_connections.TryRemove(endpoint, out var lazy))
        {
            return false;
        }

        if (lazy.IsValueCreated)
        {
            DisposeEntry(lazy.Value, "disconnect");
        }

        return true;
    }

    public IReadOnlyList<ConnectionInfo> GetConnections()
    {
        return _connections.Values
            .Where(l => l.IsValueCreated)
            .Select(l => l.Value)
            .Select(e => new ConnectionInfo(e.Endpoint, e.ConnectedAt))
            .ToList()
            .AsReadOnly();
    }

    public Task ShutdownAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        _shuttingDown = true;

        var endpoints = _connections.Keys.ToArray();
        var entries = new List<ConnectionEntry>(endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            if (_connections.TryRemove(endpoint, out var lazy) && lazy.IsValueCreated)
            {
                entries.Add(lazy.Value);
            }
        }

        var tasks = entries.Select(async entry =>
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                await entry.Channel.ShutdownAsync().WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Warn, $"gRPC channel shutdown for {entry.Endpoint} failed", ex);
            }
            finally
            {
                DisposeEntry(entry, "shutdown");
            }
        });

        return Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync(DefaultShutdownTimeout).ConfigureAwait(false);
    }

    private void DisposeEntry(ConnectionEntry entry, string reason)
    {
        try
        {
            entry.Channel.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Log(ViceLogLevel.Warn, $"gRPC {reason} channel dispose failed for {entry.Endpoint}", ex);
        }

        try
        {
            entry.Handler?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Log(ViceLogLevel.Warn, $"gRPC {reason} handler dispose failed for {entry.Endpoint}", ex);
        }
    }

    private ConnectionEntry CreateEntry(string endpoint, bool plaintext, IConsoleWriter? writer)
    {
        if (plaintext)
        {
            WarnPlaintext(_logger, endpoint, writer);
        }

        var (channel, handler) = CreateChannel(endpoint, plaintext, _logger);
        return new ConnectionEntry(channel,
                                   handler,
                                   endpoint,
                                   DateTime.UtcNow,
                                   plaintext);
    }

    private static (GrpcChannel channel, IDisposable? handler) CreateChannel(
        string endpoint, bool plaintext, IViceLogger logger)
    {
        var options = new GrpcChannelOptions
        {
            MaxReceiveMessageSize = MAX_GRPC_MESSAGE_BYTES,
            MaxSendMessageSize = MAX_GRPC_MESSAGE_BYTES,
        };

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = (context, ct) => SafeOutboundConnection.ConnectAsync(context, ct, logger),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true,
        };

        options.HttpHandler = handler;
        var scheme = plaintext ? "http" : "https";
        return (GrpcChannel.ForAddress($"{scheme}://{endpoint}", options), handler);
    }

    internal sealed record ConnectionEntry(GrpcChannel Channel,
                                           IDisposable? Handler,
                                           string Endpoint,
                                           DateTime ConnectedAt,
                                           bool Plaintext);
}
