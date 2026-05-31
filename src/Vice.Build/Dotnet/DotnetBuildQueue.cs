using System.Collections.Concurrent;
using Vice.Logging;

namespace Vice.Build.Dotnet;

public sealed class DotnetBuildQueue : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<int>>> _inflight =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IViceLogger _logger;
    private int _disposed;

    public DotnetBuildQueue(IViceLogger? logger = null)
    {
        _logger = logger ?? NullViceLogger.Instance;
    }

    public Task<int> GetOrStart(string key, Func<Task<int>> factory, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(DotnetBuildQueue));
        }

        if (_inflight.TryGetValue(key, out var existing))
        {
            _logger.Log(ViceLogLevel.Info,
                $"build queue: '{key}' already in flight ({_inflight.Count} active); waiting for the running build to finish.");
            return existing.Value.WaitAsync(ct);
        }

        var lazy = _inflight.GetOrAdd(key, k => new Lazy<Task<int>>(
            () => RunBuild(k, factory),
            LazyThreadSafetyMode.ExecutionAndPublication));

        if (_logger.IsEnabled(ViceLogLevel.Debug))
        {
            _logger.Log(ViceLogLevel.Debug, $"build queue: starting '{key}' (in-flight={_inflight.Count})");
        }

        return lazy.Value.WaitAsync(ct);
    }

    private Task<int> RunBuild(string key, Func<Task<int>> factory)
    {
        return Task.Run(async () =>
        {
            try
            {
                return await factory().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Log(ViceLogLevel.Warn, $"build queue: '{key}' faulted", ex);
                throw;
            }
            finally
            {
                if (_inflight.TryGetValue(key, out var stored))
                {
                    _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<int>>>(key, stored));
                }
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var snapshot = _inflight.Values.ToArray();

        foreach (var lazy in snapshot)
        {
            try
            {
                await lazy.Value.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Warn, "build queue: in-flight task threw during drain", ex);
            }
        }
    }
}
