using System.Threading.Channels;
using Vice.Logging;

namespace Vice.Jobs;

internal sealed class JobWorkerPool
{
    private readonly Func<JobStateHolder, Task> _executeJob;
    private readonly CancellationToken _shutdownToken;
    private readonly TimeSpan _shutdownTimeout;
    private readonly Channel<JobStateHolder> _workChannel;
    private readonly Task[] _workers;

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

        _workChannel = Channel.CreateUnbounded<JobStateHolder>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
        });

        _workers = new Task[maxConcurrency];
        for (var i = 0; i < maxConcurrency; i++)
        {
            _workers[i] = Task.Run(WorkerLoopAsync);
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

    private async Task WorkerLoopAsync()
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
        }
        catch (OperationCanceledException)
        {
            Vice.Log.Emit(ViceLogLevel.Trace, "worker loop cancelled");
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "worker loop terminated with exception", ex);
        }
    }
}
