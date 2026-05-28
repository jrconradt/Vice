namespace Vice.Mux.Sinks;

public interface ISink : IAsyncDisposable
{
    string Label { get; }
    ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct);
    ValueTask FlushAsync(CancellationToken ct);
}
