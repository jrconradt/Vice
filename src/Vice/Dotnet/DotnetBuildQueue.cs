using System.Collections.Concurrent;
using Vice.Logging;

namespace Vice.Dotnet;

internal sealed class DotnetBuildQueue : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Task<int>> _inflight =
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
            return existing.WaitAsync(ct);
        }

        var task = _inflight.GetOrAdd(key, k =>
        {
            if (_logger.IsEnabled(ViceLogLevel.Debug))
            {
                _logger.Log(ViceLogLevel.Debug, $"build queue: starting '{k}' (in-flight={_inflight.Count + 1})");
            }

            return Task.Run(async () =>
            {
                try
                {
                    return await factory().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Log(ViceLogLevel.Warn, $"build queue: '{k}' faulted", ex);
                    throw;
                }
                finally
                {
                    _inflight.TryRemove(k, out _);
                }
            });
        });

        return task.WaitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var snapshot = _inflight.Values.ToArray();

        foreach (var t in snapshot)
        {
            try
            {
                await t.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Warn, "build queue: in-flight task threw during drain", ex);
            }
        }
    }
}
