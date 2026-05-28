using Vice.Execution;

namespace Vice.Streaming;

internal sealed class StreamingLauncher<T> : IStreamingLauncher
{
    private readonly Func<IStreamingCommandContext<T>, CancellationToken, Task<int>>? _producer;
    private readonly Func<IConsumingCommandContext<T>, CancellationToken, Task<int>>? _consumer;

    public Type ItemType => typeof(T);
    public int DefaultChannelCapacity { get; }
    public bool HasProducer => _producer is not null;
    public bool HasConsumer => _consumer is not null;

    public StreamingLauncher(
        Func<IStreamingCommandContext<T>, CancellationToken, Task<int>>? producer,
        Func<IConsumingCommandContext<T>, CancellationToken, Task<int>>? consumer,
        int defaultChannelCapacity)
    {
        _producer = producer;
        _consumer = consumer;
        DefaultChannelCapacity = defaultChannelCapacity;
    }

    public (object Channel, IAsyncDisposable Disposable) CreateChannel(int capacity)
    {
        var ch = new StreamChannel<T>(capacity);
        return (ch, ch);
    }

    public void CompleteChannel(object channel) => ((StreamChannel<T>)channel).Complete();

    public void FaultChannel(object channel, Exception exception) => ((StreamChannel<T>)channel).Fault(exception);

    public Task<int> InvokeProducerAsync(CommandContext ctx, object channel, CancellationToken ct)
    {
        if (_producer is null)
        {
            throw new InvalidOperationException($"No producer registered on launcher for {typeof(T).Name}.");
        }

        var streamCtx = new StreamingCommandContext<T>(ctx, (StreamChannel<T>)channel);
        return _producer(streamCtx, ct);
    }

    public Task<int> InvokeConsumerAsync(CommandContext ctx, object channel, CancellationToken ct)
    {
        if (_consumer is null)
        {
            throw new InvalidOperationException($"No consumer registered on launcher for {typeof(T).Name}.");
        }

        var consumeCtx = new ConsumingCommandContext<T>(ctx, (StreamChannel<T>)channel);
        return _consumer(consumeCtx, ct);
    }
}
