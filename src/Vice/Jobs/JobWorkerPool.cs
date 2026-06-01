using System.Collections.Concurrent;
using System.Threading.Channels;
using Vice.Logging;

namespace Vice.Jobs;

internal sealed class JobWorkerPool
{
    public const int MAX_QUEUED_JOBS = 1024;
    public const int MAX_WORKER_RESTARTS = 64;

    private readonly Func<JobStateHolder, Task> _executeJob;
    private readonly CancellationToken _shutdownToken;
    private readonly TimeSpan _shutdownTimeout;
    private readonly Channel<JobStateHolder> _workChannel;
    private readonly Task[] _workers;
    private readonly ConcurrentDictionary<int, byte> _liveWorkers = new();
    private readonly ConcurrentQueue<byte> _restartTokens = new();
    private readonly int _configuredConcurrency;

    public int ConfiguredConcurrency => _configuredConcurrency;

    public int LiveWorkerCount => _liveWorkers.Count;

    public bool IsDegraded => _liveWorkers.Count < _configuredConcurrency;

    public JobWorkerPool(
        int maxConcurrency,
        Func<JobStateHolder, Task> executeJob,
        CancellationToken shutdownToken,
        TimeSpan shutdownTimeout)
    {
        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        _executeJob = executeJob ?? throw new ArgumentNullException(nameof(executeJob));
        _shutdownToken = shutdownToken;
        _shutdownTimeout = shutdownTimeout;
        _configuredConcurrency = maxConcurrency;
        for (var i = 0; i < MAX_WORKER_RESTARTS; i++)
        {
            _restartTokens.Enqueue(0);
        }

        _workChannel = Channel.CreateBounded<JobStateHolder>(new BoundedChannelOptions(MAX_QUEUED_JOBS)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

        _workers = new Task[maxConcurrency];
        for (var i = 0; i < maxConcurrency; i++)
        {
            var slot = i;
            _liveWorkers[slot] = 0;
            _workers[slot] = Task.Run(() => WorkerLoopAsync(slot));
        }
    }

    public ValueTask EnqueueAsync(JobStateHolder holder, CancellationToken ct)
        => _workChannel.Writer.WriteAsync(holder, ct);

    public async Task DrainAsync()
    {
        _workChannel.Writer.TryComplete();
        try
        {
            await Task.WhenAll(_workers).WaitAsync(_shutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, $"worker pool did not drain within {_shutdownTimeout}");
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "worker pool drain observed exception", ex);
        }
    }

    private async Task WorkerLoopAsync(int slot)
    {
        try
        {
            await foreach (var holder in _workChannel.Reader.ReadAllAsync(_shutdownToken).ConfigureAwait(false))
            {
                var snapshot = holder.Read();
                if (snapshot.Status is JobStatus.Failed or JobStatus.Completed)
                {
                    continue;
                }

                if (snapshot.Status == JobStatus.Queued)
                {
                    if (!holder.TryTransition(JobStatus.Running))
                    {
                        continue;
                    }
                }

                var jobId = holder.Read().Id;
                try
                {
                    await _executeJob(holder).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, $"job {jobId} worker observed unhandled exception", ex);
                }
            }

            _liveWorkers.TryRemove(slot, out _);
            Vice.Log.Emit(ViceLogLevel.Trace, $"job worker slot {slot} completed; work channel closed");
        }
        catch (OperationCanceledException)
        {
            _liveWorkers.TryRemove(slot, out _);
            Vice.Log.Emit(ViceLogLevel.Trace, $"job worker slot {slot} cancelled");
        }
        catch (Exception ex)
        {
            _liveWorkers.TryRemove(slot, out _);
            RecoverWorker(slot, ex);
        }
    }

    private void RecoverWorker(int slot, Exception ex)
    {
        if (_shutdownToken.IsCancellationRequested
            || _workChannel.Reader.Completion.IsCompleted)
        {
            Vice.Log.Emit(ViceLogLevel.Warn,
                          $"POOL_WORKER_TERMINATED job worker slot {slot} terminated during shutdown",
                          ex);
            return;
        }

        if (!_restartTokens.TryDequeue(out _))
        {
            Vice.Log.Emit(ViceLogLevel.Error,
                          $"POOL_DEGRADED job worker slot {slot} terminated and restart budget of {MAX_WORKER_RESTARTS} is exhausted; {_liveWorkers.Count} of {_configuredConcurrency} workers live",
                          ex);
            return;
        }

        Vice.Log.Emit(ViceLogLevel.Error,
                      $"POOL_WORKER_TERMINATED job worker slot {slot} terminated with exception; restarting; {_liveWorkers.Count} of {_configuredConcurrency} workers live before restart",
                      ex);

        _liveWorkers[slot] = 0;
        _workers[slot] = Task.Run(() => WorkerLoopAsync(slot));
    }
}
