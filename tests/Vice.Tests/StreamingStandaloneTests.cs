using System.Threading.Tasks;
using Vice;
using Vice.Contracts;
using Vice.Display;
using Vice.Streaming;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class StreamingStandaloneTests
{
    private static (ViceApp App, RecordingConsole Con) NewApp()
    {
        var c = new RecordingConsole();
        return (new ViceApp("vice", "1.0.0", description: null,
            console: c, status: NullStatusDisplay.Instance), c);
    }

    [Fact]
    public async Task RegisterStreaming_Standalone_DrainsToConsole()
    {
        var (app, console) = NewApp();

        app.RegisterStreaming<string>(
            verb("emit"),
            "emit",
            handler: async (ctx, ct) =>
            {
                await ctx.Stream.YieldAsync("alpha", ct);
                await ctx.Stream.YieldAsync("beta", ct);
                ctx.Stream.Complete();
                return 0;
            });

        var exit = await app.RunAsync(new[] { "emit" });

        Assert.Equal(0, exit);
        Assert.Contains("alpha", console.Output);
        Assert.Contains("beta", console.Output);
    }

    [Fact]
    public async Task RegisterStreaming_WithClassicFallback_UsesFallback_WhenStandalone()
    {
        var (app, _) = NewApp();
        var fallbackRan = false;

        app.RegisterStreaming<string>(
            verb("emit"),
            "emit",
            handler: (ctx, ct) => Task.FromResult(0),
            classicFallback: (ctx, ct) => { fallbackRan = true; return Task.FromResult(0); });

        var exit = await app.RunAsync(new[] { "emit" });

        Assert.Equal(0, exit);
        Assert.True(fallbackRan);
    }

    [Fact]
    public async Task StreamingProducer_ConsumerPair_PassesItemsThroughChannel()
    {

        await using var channel = new StreamChannel<int>(capacity: 16);

        var consumed = new List<int>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var item in ((IStreamInput<int>)channel).ReadAllAsync(default))
            {
                consumed.Add(item);
            }
        });

        await ((IStreamContext<int>)channel).YieldAsync(1);
        await ((IStreamContext<int>)channel).YieldAsync(2);
        await ((IStreamContext<int>)channel).YieldAsync(3);
        ((IStreamContext<int>)channel).Complete();

        await consumer;
        Assert.Equal(new[] { 1, 2, 3 }, consumed);
    }
}
