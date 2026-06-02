namespace Vice.Contracts;

internal interface IStreamingLauncher
{
    Type ItemType { get; }
    int DefaultChannelCapacity { get; }
    bool HasProducer { get; }
    bool HasConsumer { get; }

    (object Channel, IAsyncDisposable Disposable) CreateChannel(int capacity);
    void CompleteChannel(object channel);
    void FaultChannel(object channel, Exception exception);
    Task<int> InvokeProducerAsync(CommandContext ctx, object channel, CancellationToken ct);
    Task<int> InvokeConsumerAsync(CommandContext ctx, object channel, CancellationToken ct);
}
