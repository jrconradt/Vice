using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Vice.Concurrency;
using Vice.Logging;

namespace Vice.Net.Commands.Documents;

internal sealed class PoliteHandler : DelegatingHandler
{
    private static readonly TimeSpan DefaultMinInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EvictionScanInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _minInterval;
    private readonly int _maxRetries;
    private readonly IViceLogger _logger;
    private readonly ConcurrentDictionary<string, Lazy<HostGate>> _hostGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _evictionCts = new();
    private readonly Task _evictionLoop;

    public PoliteHandler(TimeSpan? minInterval = null, int maxRetries = 3, IViceLogger? logger = null)
        : base(new HttpClientHandler())
    {
        _minInterval = minInterval ?? DefaultMinInterval;
        _maxRetries = maxRetries;
        _logger = logger ?? NullViceLogger.Instance;
        _evictionLoop = Task.Run(() => EvictionLoopAsync(_evictionCts.Token));
    }

    public PoliteHandler(HttpMessageHandler innerHandler, TimeSpan? minInterval = null, int maxRetries = 3, IViceLogger? logger = null)
        : base(innerHandler)
    {
        _minInterval = minInterval ?? DefaultMinInterval;
        _maxRetries = maxRetries;
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

                var cutoffTicks = DateTime.UtcNow.Subtract(IdleTtl).Ticks;
                foreach (var kvp in _hostGates)
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

                    if (!_hostGates.TryRemove(kvp.Key, out var removed) || !removed.IsValueCreated)
                    {
                        continue;
                    }

                    try
                    {
                        await removed.Value.Queue.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(ViceLogLevel.Trace, $"polite handler idle eviction dispose failed for '{kvp.Key}'", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log(ViceLogLevel.Warn, "polite handler eviction loop faulted", ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                _evictionCts.Cancel();
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Trace, "polite handler eviction cancel observed exception", ex);
            }

            try
            {
                _evictionLoop.WaitAsync(DisposeWaitTimeout).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                _logger.Log(ViceLogLevel.Trace, "polite handler eviction loop did not finish within dispose timeout");
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Trace, "polite handler eviction loop dispose observed exception", ex);
            }

            try
            {
                _evictionCts.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Trace, "polite handler eviction cts dispose observed exception", ex);
            }

            foreach (var lazy in _hostGates.Values)
            {
                if (!lazy.IsValueCreated)
                {
                    continue;
                }

                try
                {
                    lazy.Value.Queue.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Log(ViceLogLevel.Trace, "polite handler host gate dispose observed exception", ex);
                }
            }
            _hostGates.Clear();
        }
        base.Dispose(disposing);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var host = request.RequestUri?.Host ?? string.Empty;
        var gate = _hostGates.GetOrAdd(host,
            _ => new Lazy<HostGate>(() => new HostGate(), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        Interlocked.Exchange(ref gate.LastUsedTicks, DateTime.UtcNow.Ticks);

        return gate.Queue.EnqueueAsync(async token =>
        {
            try
            {
                return await SendWithThrottleAndRetryAsync(gate, request, token).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref gate.LastRequestTicks, Stopwatch.GetTimestamp());
            }
        }, ct);
    }

    private async Task<HttpResponseMessage> SendWithThrottleAndRetryAsync(
        HostGate gate, HttpRequestMessage request, CancellationToken ct)
    {
        var lastTicks = Interlocked.Read(ref gate.LastRequestTicks);
        if (lastTicks != 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(lastTicks);
            if (elapsed < _minInterval)
            {
                await Task.Delay(_minInterval - elapsed, ct).ConfigureAwait(false);
            }
        }

        byte[]? bodyBytes = null;
        string? contentType = null;
        if (request.Content is not null)
        {
            bodyBytes = await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            contentType = request.Content.Headers.ContentType?.ToString();
        }

        for (var attempt = 0; ; attempt++)
        {
            var toSend = attempt == 0 ? request : CloneRequest(request, bodyBytes, contentType);
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(toSend, ct).ConfigureAwait(false);
            }
            finally
            {
                if (attempt > 0)
                {
                    toSend.Dispose();
                }
            }

            if (response.StatusCode is not (HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable))
            {
                return response;
            }

            if (attempt >= _maxRetries)
            {
                _logger.Log(ViceLogLevel.Warn,
                    $"polite: {request.RequestUri?.Host} returned {(int)response.StatusCode} {response.StatusCode}; giving up after {attempt + 1} attempts.");
                return response;
            }

            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            _logger.Log(ViceLogLevel.Info,
                $"polite: {request.RequestUri?.Host} returned {(int)response.StatusCode}; backing off {retryAfter.TotalSeconds:0.0}s then retrying (attempt {attempt + 1}/{_maxRetries}).");
            response.Dispose();

            await Task.Delay(retryAfter, ct).ConfigureAwait(false);
        }
    }

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "X-Auth-Token",
        "X-Api-Key",
    };

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original, byte[]? bodyBytes, string? contentType)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
        };
        foreach (var (key, values) in original.Headers)
        {
            if (SensitiveHeaderNames.Contains(key))
            {
                continue;
            }

            clone.Headers.TryAddWithoutValidation(key, values);
        }
        if (bodyBytes is not null)
        {
            clone.Content = new ByteArrayContent(bodyBytes);
            if (contentType is not null)
            {
                clone.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
        }
        return clone;
    }

    private sealed class HostGate
    {
        public readonly SerialQueue Queue = new();
        public long LastRequestTicks;
        public long LastUsedTicks;
    }
}
