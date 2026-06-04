using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Vice.Concurrency;
using Vice.Logging;

namespace Vice.Net.Requests.Http;

public sealed class PoliteHandler : DelegatingHandler
{
    private static readonly TimeSpan DefaultMinInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EvictionScanInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);

    private const int DefaultMaxConcurrency = 1;

    private readonly TimeSpan _minInterval;
    private readonly IReadOnlyDictionary<string, TimeSpan> _hostMinIntervals;
    private readonly int _maxConcurrency;
    private readonly IReadOnlyDictionary<string, int> _hostMaxConcurrency;
    private readonly int _maxRetries;
    private readonly IViceLogger _logger;
    private readonly ConcurrentDictionary<string, Lazy<HostGate>> _hostGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _evictionCts = new();
    private readonly Task _evictionLoop;

    public PoliteHandler(TimeSpan? minInterval = null,
                         int maxRetries = 3,
                         IViceLogger? logger = null,
                         IReadOnlyDictionary<string, TimeSpan>? hostMinIntervals = null,
                         int maxConcurrencyPerHost = DefaultMaxConcurrency,
                         IReadOnlyDictionary<string, int>? hostMaxConcurrency = null)
        : base(new HttpClientHandler())
    {
        _minInterval = minInterval ?? DefaultMinInterval;
        _hostMinIntervals = BuildHostIntervals(hostMinIntervals);
        _maxConcurrency = NormalizeConcurrency(maxConcurrencyPerHost);
        _hostMaxConcurrency = BuildHostConcurrency(hostMaxConcurrency);
        _maxRetries = maxRetries;
        _logger = logger ?? NullViceLogger.Instance;
        _evictionLoop = Task.Run(() => EvictionLoopAsync(_evictionCts.Token));
    }

    public PoliteHandler(HttpMessageHandler innerHandler,
                         TimeSpan? minInterval = null,
                         int maxRetries = 3,
                         IViceLogger? logger = null,
                         IReadOnlyDictionary<string, TimeSpan>? hostMinIntervals = null,
                         int maxConcurrencyPerHost = DefaultMaxConcurrency,
                         IReadOnlyDictionary<string, int>? hostMaxConcurrency = null)
        : base(innerHandler)
    {
        _minInterval = minInterval ?? DefaultMinInterval;
        _hostMinIntervals = BuildHostIntervals(hostMinIntervals);
        _maxConcurrency = NormalizeConcurrency(maxConcurrencyPerHost);
        _hostMaxConcurrency = BuildHostConcurrency(hostMaxConcurrency);
        _maxRetries = maxRetries;
        _logger = logger ?? NullViceLogger.Instance;
        _evictionLoop = Task.Run(() => EvictionLoopAsync(_evictionCts.Token));
    }

    private static IReadOnlyDictionary<string, TimeSpan> BuildHostIntervals(IReadOnlyDictionary<string, TimeSpan>? overrides)
    {
        if (overrides is null
            || overrides.Count == 0)
        {
            return EmptyHostIntervals;
        }

        return new Dictionary<string, TimeSpan>(overrides, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, int> BuildHostConcurrency(IReadOnlyDictionary<string, int>? overrides)
    {
        if (overrides is null
            || overrides.Count == 0)
        {
            return EmptyHostConcurrency;
        }

        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (host, value) in overrides)
        {
            normalized[host] = NormalizeConcurrency(value);
        }
        return normalized;
    }

    private static int NormalizeConcurrency(int value)
    {
        return value < 1 ? 1 : value;
    }

    private static readonly IReadOnlyDictionary<string, TimeSpan> EmptyHostIntervals =
        new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, int> EmptyHostConcurrency =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private TimeSpan IntervalForHost(string host)
    {
        if (_hostMinIntervals.TryGetValue(host, out var interval))
        {
            return interval;
        }

        return _minInterval;
    }

    private int ConcurrencyForHost(string host)
    {
        if (_hostMaxConcurrency.TryGetValue(host, out var concurrency))
        {
            return concurrency;
        }

        return _maxConcurrency;
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

                    foreach (var lane in removed.Value.Lanes)
                    {
                        try
                        {
                            await lane.Queue.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Log(ViceLogLevel.Trace, $"polite handler idle eviction dispose failed for '{kvp.Key}'", ex);
                        }
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

                foreach (var lane in lazy.Value.Lanes)
                {
                    try
                    {
                        lane.Queue.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(ViceLogLevel.Trace, "polite handler host gate dispose observed exception", ex);
                    }
                }
            }
            _hostGates.Clear();
        }
        base.Dispose(disposing);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var host = request.RequestUri?.Host ?? string.Empty;
        var laneCount = ConcurrencyForHost(host);
        while (true)
        {
            var gate = _hostGates.GetOrAdd(host,
                _ => new Lazy<HostGate>(() => new HostGate(laneCount), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            Interlocked.Exchange(ref gate.LastUsedTicks, DateTime.UtcNow.Ticks);
            var lane = gate.SelectLane();

            try
            {
                return await lane.Queue.EnqueueAsync(async token =>
                {
                    try
                    {
                        return await SendWithThrottleAndRetryAsync(lane, request, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref lane.LastRequestTicks, Stopwatch.GetTimestamp());
                    }
                }, ct).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                ct.ThrowIfCancellationRequested();
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithThrottleAndRetryAsync(
        Lane lane, HttpRequestMessage request, CancellationToken ct)
    {
        var minInterval = IntervalForHost(request.RequestUri?.Host ?? string.Empty);
        var lastTicks = Interlocked.Read(ref lane.LastRequestTicks);
        if (lastTicks != 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(lastTicks);
            if (elapsed < minInterval)
            {
                await Task.Delay(minInterval - elapsed, ct).ConfigureAwait(false);
            }
        }

        byte[]? bodyBytes = null;
        string? contentType = null;
        if (request.Content is not null)
        {
            bodyBytes = await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            contentType = request.Content.Headers.ContentType?.ToString();
        }

        var retryEligible = IsRetryEligible(request);

        for (var attempt = 0; ; attempt++)
        {
            var toSend = attempt == 0 ? request : CloneRequest(request, bodyBytes, contentType);
            HttpResponseMessage response;
            try
            {
                try
                {
                    response = await base.SendAsync(toSend, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTransientException(ex, ct))
                {
                    if (!retryEligible)
                    {
                        _logger.Log(ViceLogLevel.Warn,
                            $"polite: {request.RequestUri?.Host} request failed transiently ({ex.GetType().Name}); not auto-retrying non-idempotent {request.Method} without an idempotency key.");
                        throw;
                    }

                    if (attempt >= _maxRetries)
                    {
                        _logger.Log(ViceLogLevel.Warn,
                            $"polite: {request.RequestUri?.Host} request failed transiently ({ex.GetType().Name}); giving up after {attempt + 1} attempts.");
                        throw;
                    }

                    var transientBackoff = ComputeBackoff(null, attempt);
                    _logger.Log(ViceLogLevel.Info,
                        $"polite: {request.RequestUri?.Host} request failed transiently ({ex.GetType().Name}); backing off {transientBackoff.TotalSeconds:0.0}s then retrying (attempt {attempt + 1}/{_maxRetries}).");
                    await Task.Delay(transientBackoff, ct).ConfigureAwait(false);
                    continue;
                }
            }
            finally
            {
                if (attempt > 0)
                {
                    toSend.Dispose();
                }
            }

            if (!IsRetryableStatus(response.StatusCode))
            {
                return response;
            }

            if (!retryEligible)
            {
                _logger.Log(ViceLogLevel.Warn,
                    $"polite: {request.RequestUri?.Host} returned {(int)response.StatusCode} {response.StatusCode}; not auto-retrying non-idempotent {request.Method} without an idempotency key.");
                return response;
            }

            if (attempt >= _maxRetries)
            {
                _logger.Log(ViceLogLevel.Warn,
                    $"polite: {request.RequestUri?.Host} returned {(int)response.StatusCode} {response.StatusCode}; giving up after {attempt + 1} attempts.");
                return response;
            }

            var retryAfter = ComputeBackoff(response.Headers.RetryAfter?.Delta, attempt);
            _logger.Log(ViceLogLevel.Warn,
                $"polite: {request.RequestUri?.Host} returned {(int)response.StatusCode}; backing off {retryAfter.TotalSeconds:0.0}s then retrying (attempt {attempt + 1}/{_maxRetries}).");
            response.Dispose();

            await Task.Delay(retryAfter, ct).ConfigureAwait(false);
        }
    }

    private static bool IsRetryableStatus(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static bool IsTransientException(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return false;
        }

        return ex is HttpRequestException
            or IOException
            or TaskCanceledException
            or TimeoutException;
    }

    private static readonly HashSet<HttpMethod> IdempotentMethods = new()
    {
        HttpMethod.Get,
        HttpMethod.Head,
        HttpMethod.Put,
        HttpMethod.Delete,
        HttpMethod.Options,
        HttpMethod.Trace,
    };

    private static bool IsRetryEligible(HttpRequestMessage request)
    {
        if (IdempotentMethods.Contains(request.Method))
        {
            return true;
        }

        return request.Headers.Contains("Idempotency-Key");
    }

    private static TimeSpan ComputeBackoff(TimeSpan? retryAfter, int attempt)
    {
        if (retryAfter is TimeSpan delta)
        {
            return delta < MaxRetryAfter ? delta : MaxRetryAfter;
        }

        var capSeconds = Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, attempt + 1));
        var jittered = Random.Shared.NextDouble() * capSeconds;
        return TimeSpan.FromSeconds(jittered);
    }

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "X-Api-Key",
        "X-Auth-Token",
    };

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original, byte[]? bodyBytes, string? contentType)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
        };
        var crossOrigin = !IsSameOrigin(original.RequestUri, clone.RequestUri);
        foreach (var (key, values) in original.Headers)
        {
            if (crossOrigin && SensitiveHeaders.Contains(key))
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

    private static bool IsSameOrigin(Uri? a, Uri? b)
    {
        if (a is null
            || b is null)
        {
            return false;
        }

        return string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
            && a.Port == b.Port;
    }

    private sealed class Lane
    {
        public readonly SerialQueue Queue = new();
        public long LastRequestTicks;
    }

    private sealed class HostGate
    {
        public readonly Lane[] Lanes;
        public long NextLane;
        public long LastUsedTicks;

        public HostGate(int laneCount)
        {
            Lanes = new Lane[laneCount];
            for (var i = 0; i < laneCount; i++)
            {
                Lanes[i] = new Lane();
            }
        }

        public Lane SelectLane()
        {
            if (Lanes.Length == 1)
            {
                return Lanes[0];
            }

            var index = (Interlocked.Increment(ref NextLane) - 1) % Lanes.Length;
            return Lanes[index];
        }
    }
}
