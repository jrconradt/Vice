namespace Vice.Contracts;

public interface IStreamInput<T>
{
    IAsyncEnumerable<T> ReadAllAsync(CancellationToken ct = default);
    IAsyncEnumerable<IReadOnlyList<T>> ReadBatchesAsync(int batchSize, CancellationToken ct = default);
    IAsyncEnumerable<IReadOnlyList<T>> ReadBatchesAsync(int batchSize, TimeSpan timeout, CancellationToken ct = default);

    ValueTask<bool> WaitToReadAsync(CancellationToken ct = default);

    bool TryRead(out T item);
}
