using System.Collections.Concurrent;
using Grpc.Net.Client;
using Vice.Logging;

namespace Vice.Network.gRPC;

internal sealed class GrpcConnectionManager : IAsyncDisposable
{
    private const int MAX_GRPC_MESSAGE_BYTES = 32 * 1024 * 1024;
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EvictionScanInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EvictionLoopDisposeTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, Lazy<ConnectionEntry>> _connections = new();
    private readonly CancellationTokenSource _evictionCts = new();
    private readonly Task _evictionLoop;
    private int _shuttingDown;

    public GrpcConnectionManager(IViceLogger? logger = null)
    {
        _ = logger;
        _evictionLoop = Task.Run(() => EvictionLoopAsync(_evictionCts.Token));
    }

    private async Task EvictionLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(EvictionScanInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (Volatile.Read(ref _shuttingDown) != 0)
                {
                    continue;
                }

                var cutoffTicks = DateTime.UtcNow.Subtract(IdleTtl).Ticks;
                foreach (var kvp in _connections)
                {
                    if (!kvp.Value.IsValueCreated)
                    {
                        continue;
                    }

                    var lastUsedTicks = Interlocked.Read(ref kvp.Value.Value.LastUsedTicks);
                    if (lastUsedTicks > cutoffTicks)
                    {
                        continue;
                    }

                    if (Volatile.Read(ref _shuttingDown) != 0)
                    {
                        break;
                    }

                    if (!_connections.TryRemove(kvp.Key, out var removed) || !removed.IsValueCreated)
                    {
                        continue;
                    }

                    try
                    {
                        removed.Value.Channel.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Vice.Log.Emit(ViceLogLevel.Warn, $"gRPC idle eviction channel dispose failed for {kvp.Key}", ex);
                    }

                    try
                    {
                        removed.Value.Handler?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Vice.Log.Emit(ViceLogLevel.Warn, $"gRPC idle eviction handler dispose failed for {kvp.Key}", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "gRPC connection eviction loop faulted", ex);
        }
    }

    public static void WarnInsecure(IViceLogger? logger, string endpoint)
    {
        _ = logger;
        Vice.Log.Emit(
            ViceLogLevel.Warn,
            $"TLS certificate validation disabled for {endpoint} via --insecure flag");
        Console.Error.WriteLine(
            $"vice: WARNING: TLS certificate validation disabled for endpoint {endpoint}");
    }

    public GrpcChannel Connect(string endpoint, bool plaintext = false, bool insecure = false)
    {
        if (Volatile.Read(ref _shuttingDown) != 0)
        {
            throw new InvalidOperationException(
                $"GrpcConnectionManager is shutting down; cannot connect to {endpoint}.");
        }

        var lazy = _connections.GetOrAdd(
            endpoint,
            ep => new Lazy<ConnectionEntry>(
                () => CreateEntry(ep, plaintext, insecure),
                LazyThreadSafetyMode.ExecutionAndPublication));

        var entry = lazy.Value;
        Interlocked.Exchange(ref entry.LastUsedTicks, DateTime.UtcNow.Ticks);
        return entry.Channel;
    }

    public GrpcChannel GetChannel(string endpoint)
    {
        if (!_connections.TryGetValue(endpoint, out var lazy))
        {
            throw new InvalidOperationException($"Not connected to {endpoint}");
        }

        var entry = lazy.Value;
        Interlocked.Exchange(ref entry.LastUsedTicks, DateTime.UtcNow.Ticks);
        Interlocked.Increment(ref entry.CallCount);
        return entry.Channel;
    }

    public bool Disconnect(string endpoint)
    {
        if (!_connections.TryRemove(endpoint, out var lazy))
        {
            return false;
        }

        if (lazy.IsValueCreated)
        {
            try
            {
                lazy.Value.Channel.Dispose();
                lazy.Value.Handler?.Dispose();
            }
            catch (Exception ex)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, $"gRPC channel dispose for {endpoint} failed", ex);
            }
        }
        return true;
    }

    public IReadOnlyList<ConnectionInfo> GetConnections()
    {
        return _connections.Values
            .Where(l => l.IsValueCreated)
            .Select(l => l.Value)
            .Select(e => new ConnectionInfo(e.Endpoint, e.ConnectedAt, Volatile.Read(ref e.CallCount)))
            .ToList()
            .AsReadOnly();
    }

    public Task ShutdownAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _shuttingDown, 1);

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
                Vice.Log.Emit(ViceLogLevel.Warn, $"gRPC channel shutdown for {entry.Endpoint} failed", ex);
            }
            finally
            {
                try
                {
                    entry.Channel.Dispose();
                }
                catch (Exception ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, $"gRPC channel dispose for {entry.Endpoint} failed", ex);
                }

                try
                {
                    entry.Handler?.Dispose();
                }
                catch (Exception ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, $"gRPC handler dispose for {entry.Endpoint} failed", ex);
                }
            }
        });

        return Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _evictionCts.Cancel();
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Trace, "gRPC connection eviction cancel observed exception", ex);
        }

        try
        {
            await _evictionLoop.WaitAsync(EvictionLoopDisposeTimeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
            Vice.Log.Emit(ViceLogLevel.Trace, "gRPC connection eviction loop did not finish within dispose timeout");
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Trace, "gRPC connection eviction loop dispose observed exception", ex);
        }

        try
        {
            _evictionCts.Dispose();
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Trace, "gRPC connection eviction cts dispose observed exception", ex);
        }

        await ShutdownAsync(DefaultShutdownTimeout).ConfigureAwait(false);
    }

    private ConnectionEntry CreateEntry(string endpoint, bool plaintext, bool insecure)
    {
        var (channel, handler) = CreateChannel(endpoint, plaintext, insecure);
        return new ConnectionEntry
        {
            Channel = channel,
            Handler = handler,
            Endpoint = endpoint,
            ConnectedAt = DateTime.UtcNow,
            CallCount = 0,
            Plaintext = plaintext,
            Insecure = insecure
        };
    }

    private static (GrpcChannel channel, IDisposable? handler) CreateChannel(
        string endpoint, bool plaintext, bool insecure)
    {
        var options = new GrpcChannelOptions
        {
            MaxReceiveMessageSize = MAX_GRPC_MESSAGE_BYTES,
            MaxSendMessageSize = MAX_GRPC_MESSAGE_BYTES,
        };

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = SafeOutboundConnection.ConnectAsync,
        };

        if (insecure)
        {
            handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            };
        }

        options.HttpHandler = handler;
        var scheme = plaintext ? "http" : "https";
        return (GrpcChannel.ForAddress($"{scheme}://{endpoint}", options), handler);
    }

    private class ConnectionEntry
    {
        public required GrpcChannel Channel { get; init; }
        public required string Endpoint { get; init; }
        public required DateTime ConnectedAt { get; init; }
        public int CallCount;
        public long LastUsedTicks;
        public required bool Plaintext { get; init; }
        public required bool Insecure { get; init; }
        public IDisposable? Handler { get; init; }
    }
}
