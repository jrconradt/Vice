namespace Vice.Streaming;

public interface IStreamContext<T>
{
    bool TryYield(T item);
    ValueTask YieldAsync(T item, CancellationToken ct = default);
    ValueTask YieldManyAsync(IEnumerable<T> items, CancellationToken ct = default);
    void Complete();
    void Fault(Exception exception);
}
