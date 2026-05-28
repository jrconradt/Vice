namespace Vice.Mux.Sinks;

internal sealed class NullSink : ISink
{
    public string Label => "null:";
    public ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct) => ValueTask.CompletedTask;
    public ValueTask FlushAsync(CancellationToken ct) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
