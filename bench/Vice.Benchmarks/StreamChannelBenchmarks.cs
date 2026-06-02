using BenchmarkDotNet.Attributes;
using Vice.Streaming;

namespace Vice.Benchmarks;

[MemoryDiagnoser]
public class StreamChannelBenchmarks
{
    [Params(1_000, 50_000)]
    public int ItemCount;

    [Params(64, 1_024)]
    public int Capacity;

    private string[] _payload = Array.Empty<string>();

    [GlobalSetup]
    public void Setup()
    {
        _payload = new string[ItemCount];
        for (var i = 0; i < ItemCount; i++)
        {
            _payload[i] = $"line-{i}";
        }
    }

    [Benchmark]
    public async Task<long> ProducerConsumerReadAll()
    {
        await using var channel = new StreamChannel<string>(Capacity);
        var producer = ProduceAsync(channel);

        long observed = 0;
        await foreach (var item in channel.ReadAllAsync())
        {
            observed += item.Length;
        }

        await producer.ConfigureAwait(false);
        return observed;
    }

    [Benchmark]
    public async Task<int> ProducerConsumerBatched()
    {
        await using var channel = new StreamChannel<string>(Capacity);
        var producer = ProduceAsync(channel);

        var batches = 0;
        await foreach (var batch in channel.ReadBatchesAsync(256))
        {
            batches += batch.Count;
        }

        await producer.ConfigureAwait(false);
        return batches;
    }

    [Benchmark]
    public async Task<int> ProducerBridgeDrain()
    {
        await using var channel = new StreamChannel<string>(Capacity);
        var producer = ProduceAsync(channel);

        var drained = await StreamBridge.DrainToStringAsync<string>(channel, CancellationToken.None)
            .ConfigureAwait(false);

        await producer.ConfigureAwait(false);
        return drained.Length;
    }

    private async Task ProduceAsync(StreamChannel<string> channel)
    {
        await Task.Yield();
        foreach (var item in _payload)
        {
            await channel.YieldAsync(item).ConfigureAwait(false);
        }

        channel.Complete();
    }
}
