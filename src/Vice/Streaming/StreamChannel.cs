using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Vice.Streaming;

internal sealed class StreamChannel<T> : IStreamContext<T>, IStreamInput<T>, IAsyncDisposable
{
    private readonly Channel<T> _channel;

    public StreamChannel(int capacity = 100)
    {
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public bool TryYield(T item) => _channel.Writer.TryWrite(item);

    public ValueTask YieldAsync(T item, CancellationToken ct = default)
    {
        if (_channel.Writer.TryWrite(item))
        {
            return ValueTask.CompletedTask;
        }

        return _channel.Writer.WriteAsync(item, ct);
    }

    public async ValueTask YieldManyAsync(IEnumerable<T> items, CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            if (!_channel.Writer.TryWrite(item))
            {
                await _channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
            }
        }
    }

    public void Complete()
        => _channel.Writer.TryComplete();

    public void Fault(Exception exception)
        => _channel.Writer.TryComplete(exception);

    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct);

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct = default)
        => _channel.Reader.WaitToReadAsync(ct);

    public bool TryRead(out T item)
        => _channel.Reader.TryRead(out item!);

    public IAsyncEnumerable<IReadOnlyList<T>> ReadBatchesAsync(int batchSize, CancellationToken ct = default)
        => ReadBatchesAsync(batchSize, Timeout.InfiniteTimeSpan, ct);

    public async IAsyncEnumerable<IReadOnlyList<T>> ReadBatchesAsync(
        int batchSize, TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var batch = new List<T>(batchSize);
        var reader = _channel.Reader;
        var useTimeout = timeout != Timeout.InfiniteTimeSpan && timeout > TimeSpan.Zero;
        var done = false;
        CancellationTokenSource? timeoutCts = useTimeout
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        try
        {
            while (!done && !ct.IsCancellationRequested)
            {
                var channelClosed = false;

                try
                {
                    while (batch.Count < batchSize)
                    {
                        if (useTimeout)
                        {
                            if (batch.Count == 0)
                            {
                                timeoutCts!.CancelAfter(timeout);
                            }

                            try
                            {
                                batch.Add(await reader.ReadAsync(timeoutCts!.Token).ConfigureAwait(false));
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                if (timeoutCts!.IsCancellationRequested)
                                {
                                    timeoutCts.Dispose();
                                    timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                }

                                break;
                            }
                        }
                        else
                        {
                            batch.Add(await reader.ReadAsync(ct).ConfigureAwait(false));
                        }
                    }
                }
                catch (ChannelClosedException)
                {
                    channelClosed = true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    done = true;
                }

                if (batch.Count > 0)
                {
                    var toYield = batch;
                    batch = new List<T>(batchSize);
                    yield return toYield;
                }

                if (channelClosed || done)
                {
                    yield break;
                }

                if (batch.Count == 0 && reader.Completion.IsCompleted)
                {
                    yield break;
                }
            }
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
