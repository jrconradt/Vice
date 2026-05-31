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
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private long _lastSavedSignature = UnsetSignature;
    private int _drained;

    private const long UnsetSignature = unchecked((long)0xFFFFFFFFFFFFFFFFUL);

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

    public async Task FlushNowAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _drained) != 0)
        {
            return;
        }

        var snapshot = _snapshotProvider();
        await SaveSerializedAsync(snapshot, ct).ConfigureAwait(false);
    }

    public async Task DrainAndFlushAsync(IReadOnlyList<JobState> finalSnapshot, CancellationToken ct)
    {
        Volatile.Write(ref _drained, 1);
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

        try
        {
            await SaveSerializedAsync(finalSnapshot, ct, force: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "final job persistence on shutdown failed", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _drained, 1);
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

        _saveGate.Dispose();
    }

    private Task SaveSerializedAsync(IReadOnlyList<JobState> snapshot, CancellationToken ct)
        => SaveSerializedAsync(snapshot, ct, force: false);

    private async Task SaveSerializedAsync(IReadOnlyList<JobState> snapshot, CancellationToken ct, bool force)
    {
        var signature = ComputeSignature(snapshot);
        await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!force
                && signature == Volatile.Read(ref _lastSavedSignature))
            {
                return;
            }

            await _persistence.SaveAsync(snapshot, ct).ConfigureAwait(false);
            Volatile.Write(ref _lastSavedSignature, signature);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static long ComputeSignature(IReadOnlyList<JobState> snapshot)
    {
        unchecked
        {
            long hash = 1469598103934665603L;
            const long prime = 1099511628211L;

            hash = (hash ^ snapshot.Count) * prime;
            foreach (var job in snapshot)
            {
                hash = (hash ^ job.Id) * prime;
                hash = (hash ^ (int)job.Status) * prime;
                hash = (hash ^ job.BytesDownloaded) * prime;
                hash = (hash ^ (job.TotalBytes ?? -1L)) * prime;
                hash = (hash ^ job.MessagesReceived) * prime;
                hash = (hash ^ (job.ErrorMessage?.GetHashCode() ?? 0)) * prime;
                hash = (hash ^ (job.CompletedAt?.Ticks ?? 0L)) * prime;
                hash = (hash ^ (job.StartedAt?.Ticks ?? 0L)) * prime;
                hash = (hash ^ (job.LastProgressAt?.Ticks ?? 0L)) * prime;
            }

            return hash == UnsetSignature ? 0L : hash;
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
                    await SaveSerializedAsync(snapshot, _shutdownToken).ConfigureAwait(false);
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
