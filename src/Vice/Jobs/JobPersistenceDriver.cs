using System.Threading.Channels;
using Vice.Logging;

namespace Vice.Jobs;

internal sealed class JobPersistenceDriver : IAsyncDisposable
{
    private readonly JobPersistence _persistence;
    private readonly CancellationToken _shutdownToken;
    private readonly Channel<bool> _persistDirty;
    private readonly Task _persistWorker;
    private readonly Func<IReadOnlyList<JobState>> _snapshotProvider;
    private readonly TimeSpan _shutdownTimeout;

    public JobPersistenceDriver(
        JobPersistence persistence,
        Func<IReadOnlyList<JobState>> snapshotProvider,
        CancellationToken shutdownToken,
        TimeSpan shutdownTimeout)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _shutdownToken = shutdownToken;
        _shutdownTimeout = shutdownTimeout;

        _persistDirty = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
        _persistWorker = Task.Run(PersistLoopAsync);
    }

    public void SignalDirty()
        => _persistDirty.Writer.TryWrite(true);

    public async Task FlushAsync(IReadOnlyList<JobState> finalSnapshot, CancellationToken ct)
    {
        try
        {
            await _persistence.SaveAsync(finalSnapshot, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "final job persistence on shutdown failed", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _persistDirty.Writer.TryComplete();
        try
        {
            await _persistWorker.WaitAsync(_shutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, $"persistence worker did not drain within {_shutdownTimeout}");
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "persistence worker drain observed exception", ex);
        }
    }

    private async Task PersistLoopAsync()
    {
        try
        {
            await foreach (var _ in _persistDirty.Reader.ReadAllAsync(_shutdownToken).ConfigureAwait(false))
            {
                try
                {
                    await Task.Delay(200, _shutdownToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var snapshot = _snapshotProvider();
                try
                {
                    await _persistence.SaveAsync(snapshot, _shutdownToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, "job persistence save failed", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Vice.Log.Emit(ViceLogLevel.Trace, "persistence loop cancelled");
        }
    }
}
