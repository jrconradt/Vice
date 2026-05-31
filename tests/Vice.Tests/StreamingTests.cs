using System.Threading.Tasks;
using Vice;
using Vice.Lexicon;
using Vice.Display;
using Vice.Streaming;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class StreamingTests
{
    [Fact]
    public async Task StreamChannel_SynchronousHotLoop_StaysWithinAllocationBudget()
    {
        const int WarmupItems = 4_096;
        const int MeasuredItems = 262_144;
        const long MaxBytesPerItem = 8;

        await DrainSynchronousAsync(WarmupItems);

        var before = GC.GetAllocatedBytesForCurrentThread();
        await DrainSynchronousAsync(MeasuredItems);
        var after = GC.GetAllocatedBytesForCurrentThread();

        var perItem = (after - before) / (double)MeasuredItems;
        Assert.True(perItem <= MaxBytesPerItem,
            $"StreamChannel hot loop allocated {perItem:0.00} bytes/item (budget {MaxBytesPerItem}); a regression doubled allocations.");
    }

    [Fact]
    public async Task StreamChannel_PrefilledBatchRead_StaysWithinAllocationBudget()
    {
        const int WarmupItems = 4_096;
        const int MeasuredItems = 131_072;
        const int BatchSize = 64;
        const long MaxBytesPerItem = 64;

        await DrainPrefilledBatchedAsync(WarmupItems, BatchSize);

        var before = GC.GetAllocatedBytesForCurrentThread();
        await DrainPrefilledBatchedAsync(MeasuredItems, BatchSize);
        var after = GC.GetAllocatedBytesForCurrentThread();

        var perItem = (after - before) / (double)MeasuredItems;
        Assert.True(perItem <= MaxBytesPerItem,
            $"StreamChannel batch loop allocated {perItem:0.00} bytes/item (budget {MaxBytesPerItem}); a regression doubled allocations.");
    }

    private static async Task DrainSynchronousAsync(int items)
    {
        await using var channel = new StreamChannel<int>(capacity: 1024);

        var consumed = 0;
        for (var i = 0; i < items; i++)
        {
            while (!channel.TryYield(i))
            {
                while (channel.TryRead(out _))
                {
                    consumed++;
                }
            }
        }

        channel.Complete();
        while (channel.TryRead(out _))
        {
            consumed++;
        }

        Assert.Equal(items, consumed);
    }

    private static async Task DrainPrefilledBatchedAsync(int items, int batchSize)
    {
        await using var channel = new StreamChannel<int>(capacity: items);

        for (var i = 0; i < items; i++)
        {
            Assert.True(channel.TryYield(i));
        }

        channel.Complete();

        var consumed = 0;
        await foreach (var batch in channel.ReadBatchesAsync(batchSize))
        {
            consumed += batch.Count;
        }

        Assert.Equal(items, consumed);
    }

    [Fact]
    public async Task RegisterStreamingPipeline_ProducerToConsumer_RoundTrip()
    {
        var console = new RecordingConsole();
        var app = new ViceApp("vice", "1.0.0", description: null,
            console: console, status: NullStatusDisplay.Instance);

        var received = new List<int>();

        app.RegisterStreamingPipeline<int>(
            verb("produce") > Connectors.Then() > noun("consume"),
            "produce/consume",
            producer: async (ctx, ct) =>
            {
                for (int i = 0; i < 5; i++)
                {
                    await ctx.Stream.YieldAsync(i, ct);
                }

                ctx.Stream.Complete();
                return 0;
            },
            consumer: async (ctx, ct) =>
            {
                await foreach (var item in ctx.Input.ReadAllAsync(ct))
                {
                    received.Add(item);
                }

                return 0;
            });

        var exit = await app.RunAsync(new[] { "produce", "then", "consume" });

        Assert.Equal(0, exit);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, received);
    }

    [Fact]
    public async Task RegisterStreamingPipeline_ConsumerSeesBatch()
    {
        var console = new RecordingConsole();
        var app = new ViceApp("vice", "1.0.0", description: null,
            console: console, status: NullStatusDisplay.Instance);

        var batchSizes = new List<int>();

        app.RegisterStreamingPipeline<int>(
            verb("emit") > Connectors.Then() > noun("batched"),
            "batched",
            producer: async (ctx, ct) =>
            {
                for (int i = 0; i < 7; i++)
                {
                    await ctx.Stream.YieldAsync(i, ct);
                }

                ctx.Stream.Complete();
                return 0;
            },
            consumer: async (ctx, ct) =>
            {
                await foreach (var batch in ctx.Input.ReadBatchesAsync(3, ct))
                {
                    batchSizes.Add(batch.Count);
                }

                return 0;
            },
            options: new BatchOptions(BatchSize: 3));

        var exit = await app.RunAsync(new[] { "emit", "then", "batched" });

        Assert.Equal(0, exit);
        Assert.Equal(7, batchSizes.Sum());
    }
}
