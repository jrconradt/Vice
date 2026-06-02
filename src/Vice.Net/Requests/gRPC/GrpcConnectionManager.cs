using System.Collections.Concurrent;
using Grpc.Net.Client;
using Vice.Display.Rendering;
using Vice.Logging;

namespace Vice.Net.Requests.Grpc;

public sealed class GrpcConnectionManager : IAsyncDisposable
{
    private const int MAX_GRPC_MESSAGE_BYTES = 32 * 1024 * 1024;
    private const int MAX_CONNECTIONS = 256;
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EvictionScanInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EvictionLoopDisposeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EvictionDrainTimeout = TimeSpan.FromMinutes(35);

    private readonly ConcurrentDictionary<string, Lazy<ConnectionEntry>> _connections = new();
    private readonly ConcurrentDictionary<Task, byte> _drainTasks = new();
    private readonly CancellationTokenSource _evictionCts = new();
    private readonly Task _evictionLoop;
    private readonly IViceLogger _logger;
    private int _shuttingDown;

    public GrpcConnectionManager(IViceLogger? logger = null)
    {
        _logger = logger ?? NullViceLogger.Instance;
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

                    if (Volatile.Read(ref _shuttingDown) != 0)
                    {
                        break;
                    }

                    var entry = kvp.Value.Value;
                    if (RetireIfIdle(entry, cutoffTicks) is null)
                    {
                        continue;
                    }

                    _connections.TryRemove(kvp);
                    DrainAndDispose(kvp.Key, entry, "idle eviction");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log(ViceLogLevel.Warn, "gRPC connection eviction loop faulted", ex);
        }
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
        if (Volatile.Read(ref _shuttingDown) != 0)
        {
            throw new InvalidOperationException(
                $"GrpcConnectionManager is shutting down; cannot connect to {endpoint}.");
        }

        while (true)
        {
            var lazy = _connections.GetOrAdd(
                endpoint,
                ep => new Lazy<ConnectionEntry>(
                    () => CreateEntry(ep, plaintext, writer),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            var entry = lazy.Value;
            if (entry.TryUpdate(Touch) is null)
            {
                _connections.TryRemove(new KeyValuePair<string, Lazy<ConnectionEntry>>(endpoint, lazy));
                continue;
            }

            if (entry.Plaintext != plaintext)
            {
                throw new InvalidOperationException(
                    $"Cached connection to {endpoint} was established with plaintext={entry.Plaintext}; refusing to reuse it for a request with plaintext={plaintext}.");
            }

            EnforceMaxConnections(endpoint);
            return entry.Channel;
        }
    }

    private void EnforceMaxConnections(string keepEndpoint)
    {
        while (_connections.Count > MAX_CONNECTIONS)
        {
            KeyValuePair<string, Lazy<ConnectionEntry>> lru = default;
            var lruTicks = long.MaxValue;
            var found = false;
            foreach (var kvp in _connections)
            {
                if (kvp.Key == keepEndpoint || !kvp.Value.IsValueCreated)
                {
                    continue;
                }

                var state = kvp.Value.Value.Read();
                if (state.Retired || state.LeaseCount > 0)
                {
                    continue;
                }

                if (state.LastUsedTicks < lruTicks)
                {
                    lruTicks = state.LastUsedTicks;
                    lru = kvp;
                    found = true;
                }
            }

            if (!found)
            {
                break;
            }

            var entry = lru.Value.Value;
            if (RetireIfIdle(entry, long.MaxValue) is null)
            {
                continue;
            }

            _connections.TryRemove(lru);
            DrainAndDispose(lru.Key, entry, "LRU eviction");
        }
    }

    private void DrainAndDispose(string endpoint, ConnectionEntry entry, string reason)
    {
        var task = DrainAndDisposeAsync(endpoint, entry, reason, _logger);
        if (!task.IsCompleted)
        {
            _drainTasks.TryAdd(task, 0);
            task.ContinueWith(
                static (t, state) =>
                {
                    var (map, finished) = ((ConcurrentDictionary<Task, byte>, Task))state!;
                    map.TryRemove(finished, out _);
                },
                (_drainTasks, task),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static async Task DrainAndDisposeAsync(
        string endpoint,
        ConnectionEntry entry,
        string reason,
        IViceLogger logger)
    {
        try
        {
            await entry.Channel.ShutdownAsync().WaitAsync(EvictionDrainTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Log(ViceLogLevel.Warn, $"gRPC {reason} channel drain failed for {endpoint}", ex);
        }

        try
        {
            entry.Channel.Dispose();
        }
        catch (Exception ex)
        {
            logger.Log(ViceLogLevel.Warn, $"gRPC {reason} channel dispose failed for {endpoint}", ex);
        }

        try
        {
            entry.Handler?.Dispose();
        }
        catch (Exception ex)
        {
            logger.Log(ViceLogLevel.Warn, $"gRPC {reason} handler dispose failed for {endpoint}", ex);
        }
    }

    public GrpcChannel GetChannel(string endpoint)
    {
        if (!_connections.TryGetValue(endpoint, out var lazy))
        {
            throw new InvalidOperationException($"Not connected to {endpoint}");
        }

        var entry = lazy.Value;
        if (entry.TryUpdate(RecordCall) is null)
        {
            throw new InvalidOperationException($"Not connected to {endpoint}");
        }

        return entry.Channel;
    }

    public ConnectionLease LeaseChannel(string endpoint)
    {
        if (!_connections.TryGetValue(endpoint, out var lazy))
        {
            throw new InvalidOperationException($"Not connected to {endpoint}");
        }

        var entry = lazy.Value;
        if (entry.TryUpdate(AcquireLease) is null)
        {
            throw new InvalidOperationException($"Not connected to {endpoint}");
        }

        return new ConnectionLease(this, entry);
    }

    public bool Disconnect(string endpoint)
    {
        if (!_connections.TryRemove(endpoint, out var lazy))
        {
            return false;
        }

        if (!lazy.IsValueCreated)
        {
            return true;
        }

        var entry = lazy.Value;
        var retired = entry.TryUpdate(Retire);
        if (retired is null)
        {
            return true;
        }

        if (retired.LeaseCount == 0)
        {
            DrainAndDispose(endpoint, entry, "disconnect");
        }

        return true;
    }

    public IReadOnlyList<ConnectionInfo> GetConnections()
    {
        return _connections.Values
            .Where(l => l.IsValueCreated)
            .Select(l => (Entry: l.Value, State: l.Value.Read()))
            .Where(p => !p.State.Retired)
            .Select(p => new ConnectionInfo(p.Entry.Endpoint, p.Entry.ConnectedAt, p.State.CallCount))
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
            if (!_connections.TryRemove(endpoint, out var lazy) || !lazy.IsValueCreated)
            {
                continue;
            }

            var entry = lazy.Value;
            var retired = entry.TryUpdate(Retire);
            if (retired is null || retired.LeaseCount > 0)
            {
                continue;
            }

            entries.Add(entry);
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
                try
                {
                    entry.Channel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Log(ViceLogLevel.Warn, $"gRPC channel dispose for {entry.Endpoint} failed", ex);
                }

                try
                {
                    entry.Handler?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Log(ViceLogLevel.Warn, $"gRPC handler dispose for {entry.Endpoint} failed", ex);
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
            _logger.Log(ViceLogLevel.Trace, "gRPC connection eviction cancel observed exception", ex);
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
            _logger.Log(ViceLogLevel.Trace, "gRPC connection eviction loop did not finish within dispose timeout");
        }
        catch (Exception ex)
        {
            _logger.Log(ViceLogLevel.Trace, "gRPC connection eviction loop dispose observed exception", ex);
        }

        try
        {
            _evictionCts.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Log(ViceLogLevel.Trace, "gRPC connection eviction cts dispose observed exception", ex);
        }

        await ShutdownAsync(DefaultShutdownTimeout).ConfigureAwait(false);

        var drains = _drainTasks.Keys.ToArray();
        if (drains.Length > 0)
        {
            try
            {
                await Task.WhenAll(drains).WaitAsync(DefaultShutdownTimeout).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Trace, "gRPC eviction drain tasks did not finish within dispose timeout", ex);
            }
        }
    }

    private void Release(ConnectionEntry entry)
    {
        var released = entry.Update(s => s with
        {
            LeaseCount = s.LeaseCount - 1,
            LastUsedTicks = DateTime.UtcNow.Ticks
        });

        if (released.Retired && released.LeaseCount == 0)
        {
            DrainAndDispose(entry.Endpoint, entry, "deferred disposal after lease release");
        }
    }

    private static LifecycleState? Touch(LifecycleState state)
        => state.Retired ? null : state with { LastUsedTicks = DateTime.UtcNow.Ticks };

    private static LifecycleState? RecordCall(LifecycleState state)
        => state.Retired
            ? null
            : state with { CallCount = state.CallCount + 1, LastUsedTicks = DateTime.UtcNow.Ticks };

    private static LifecycleState? AcquireLease(LifecycleState state)
        => state.Retired
            ? null
            : state with
            {
                LeaseCount = state.LeaseCount + 1,
                CallCount = state.CallCount + 1,
                LastUsedTicks = DateTime.UtcNow.Ticks
            };

    private static LifecycleState? Retire(LifecycleState state)
        => state.Retired ? null : state with { Retired = true };

    private static LifecycleState? RetireIfIdle(ConnectionEntry entry, long cutoffTicks)
        => entry.TryUpdate(state =>
        {
            if (state.Retired || state.LeaseCount > 0
                || state.LastUsedTicks > cutoffTicks)
            {
                return null;
            }

            return state with { Retired = true };
        });

    private ConnectionEntry CreateEntry(string endpoint, bool plaintext, IConsoleWriter? writer)
    {
        if (plaintext)
        {
            WarnPlaintext(_logger, endpoint, writer);
        }

        var (channel, handler) = CreateChannel(endpoint, plaintext);
        return new ConnectionEntry
        {
            Channel = channel,
            Handler = handler,
            Endpoint = endpoint,
            ConnectedAt = DateTime.UtcNow,
            Plaintext = plaintext
        };
    }

    private static (GrpcChannel channel, IDisposable? handler) CreateChannel(
        string endpoint, bool plaintext)
    {
        var options = new GrpcChannelOptions
        {
            MaxReceiveMessageSize = MAX_GRPC_MESSAGE_BYTES,
            MaxSendMessageSize = MAX_GRPC_MESSAGE_BYTES,
        };

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = SafeOutboundConnection.ConnectAsync,
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

    public sealed class ConnectionLease : IDisposable
    {
        private readonly GrpcConnectionManager _manager;
        private readonly ConnectionEntry _entry;
        private int _disposed;

        internal ConnectionLease(GrpcConnectionManager manager, ConnectionEntry entry)
        {
            _manager = manager;
            _entry = entry;
        }

        public GrpcChannel Channel => _entry.Channel;

        public void Renew()
        {
            _entry.TryUpdate(Touch);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _manager.Release(_entry);
        }
    }

    internal sealed class ConnectionEntry
    {
        private LifecycleState _state = new(0, 0, DateTime.UtcNow.Ticks, false);

        public required GrpcChannel Channel { get; init; }
        public required string Endpoint { get; init; }
        public required DateTime ConnectedAt { get; init; }
        public required bool Plaintext { get; init; }
        public IDisposable? Handler { get; init; }

        public LifecycleState Read() => Volatile.Read(ref _state);

        public LifecycleState? TryUpdate(Func<LifecycleState, LifecycleState?> mutator)
        {
            LifecycleState current, next;
            do
            {
                current = Volatile.Read(ref _state);
                var result = mutator(current);
                if (result is null)
                {
                    return null;
                }

                next = result;
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current));
            return next;
        }

        public LifecycleState Update(Func<LifecycleState, LifecycleState> mutator)
        {
            LifecycleState current, next;
            do
            {
                current = Volatile.Read(ref _state);
                next = mutator(current);
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current));
            return next;
        }
    }

    internal sealed record LifecycleState(int LeaseCount,
                                          int CallCount,
                                          long LastUsedTicks,
                                          bool Retired);
}
