using System.Threading.Channels;

namespace Vice.Concurrency;

internal sealed class SerialQueue : IAsyncDisposable
{
    private const int DefaultCapacity = 1024;

    private readonly Channel<Func<Task>> _channel;
    private readonly Task _worker;
    private int _disposed;

    public SerialQueue(int capacity = DefaultCapacity)
    {
        _channel = Channel.CreateBounded<Func<Task>>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _worker = Task.Run(DrainAsync);
    }

    public Task EnqueueAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
        => EnqueueAsync<object?>(async c =>
        {
            await work(c).ConfigureAwait(false);
            return null;
        }, ct);

    public async Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SerialQueue));
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> op = async () =>
        {
            try
            {
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(ct);
                    return;
                }
                var result = await work(ct).ConfigureAwait(false);
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException oce)
            {
                tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };

        await _channel.Writer.WriteAsync(op, ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task DrainAsync()
    {
        await foreach (var op in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await op().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
