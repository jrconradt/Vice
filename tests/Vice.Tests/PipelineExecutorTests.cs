using System.Threading.Tasks;
using Vice;
using Vice.Display;
using Vice.Execution;
using Vice.Lexicon;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class PipelineExecutorTests
{
    private static ViceApp NewApp(out RecordingConsole console)
    {
        console = new RecordingConsole();
        return new ViceApp("vice", "1.0.0", description: null,
            console: console, status: NullStatusDisplay.Instance);
    }

    [Fact]
    public async Task Then_PipesStage1OutputAsInputToStage2()
    {
        var app = NewApp(out var console);
        string? stage2Input = null;

        app.RegisterPipeline(
            verb("emit") > Connectors.Then() > noun("consume"),
            "pipeline",
            defaultHandler: (ctx, ct) => Task.FromResult(0),
            stageHandlers: new()
            {
                [0] = (ctx, ct) => { ctx.Console.Write("alpha"); return Task.FromResult(0); },
                [1] = (ctx, ct) => { stage2Input = ctx.PipelineInput; return Task.FromResult(0); }
            });

        Assert.Equal(0, await app.RunAsync(new[] { "emit", "then", "consume" }));
        Assert.Equal("alpha", stage2Input);
    }

    [Fact]
    public async Task Implicit_then_pipes_upstream_output_to_downstream()
    {
        var app = NewApp(out _);
        string? stage2Input = null;

        app.RegisterPipeline(
            verb("emit") > Connectors.Then() > noun("consume"),
            "implicit pipeline",
            defaultHandler: (ctx, ct) => Task.FromResult(0),
            stageHandlers: new()
            {
                [0] = (ctx, ct) => { ctx.Console.Write("alpha"); return Task.FromResult(0); },
                [1] = (ctx, ct) => { stage2Input = ctx.PipelineInput; return Task.FromResult(0); }
            });

        Assert.Equal(0, await app.RunAsync(new[] { "emit", "consume" }));
        Assert.Equal("alpha", stage2Input);
    }

    [Fact]
    public async Task Or_RunsSecondStage_OnlyWhenFirstFails()
    {
        var app = NewApp(out _);
        var fallbackCalled = false;

        app.RegisterPipeline(
            verb("try") > Connectors.Or() > noun("fallback"),
            "or pipeline",
            defaultHandler: (ctx, ct) => Task.FromResult(0),
            stageHandlers: new()
            {
                [0] = (ctx, ct) => Task.FromResult(1),
                [1] = (ctx, ct) => { fallbackCalled = true; return Task.FromResult(0); }
            });

        await app.RunAsync(new[] { "try", "or", "fallback" });
        Assert.True(fallbackCalled);
    }

    [Fact]
    public async Task Or_SkipsSecondStage_WhenFirstSucceeds()
    {
        var app = NewApp(out _);
        var fallbackCalled = false;

        app.RegisterPipeline(
            verb("try") > Connectors.Or() > noun("fallback"),
            "or pipeline",
            defaultHandler: (ctx, ct) => Task.FromResult(0),
            stageHandlers: new()
            {
                [0] = (ctx, ct) => Task.FromResult(0),
                [1] = (ctx, ct) => { fallbackCalled = true; return Task.FromResult(0); }
            });

        await app.RunAsync(new[] { "try", "or", "fallback" });
        Assert.False(fallbackCalled);
    }

    [Fact]
    public async Task And_RunsBothStages_AndConcatenatesOutput()
    {
        var app = NewApp(out _);
        var capturedSecondInput = "";

        app.RegisterPipeline(
            verb("first") > Connectors.AndPipe() > noun("second"),
            "and pipeline",
            defaultHandler: (ctx, ct) => Task.FromResult(0),
            stageHandlers: new()
            {
                [0] = (ctx, ct) => { ctx.Console.Write("A"); return Task.FromResult(0); },
                [1] = (ctx, ct) =>
                {
                    capturedSecondInput = ctx.PipelineInput ?? "";
                    ctx.Console.Write("B");
                    return Task.FromResult(0);
                }
            });

        var exit = await app.RunAsync(new[] { "first", "and", "second" });
        Assert.Equal(0, exit);
        Assert.Equal("A", capturedSecondInput);
    }

    [Fact]
    public async Task Sequential_ShortCircuits_OnFailure()
    {
        var app = NewApp(out _);
        var stage2Called = false;

        app.RegisterPipeline(
            verb("a") > Connectors.Then() > noun("b"),
            "seq",
            defaultHandler: (ctx, ct) => Task.FromResult(0),
            stageHandlers: new()
            {
                [0] = (ctx, ct) => Task.FromResult(7),
                [1] = (ctx, ct) => { stage2Called = true; return Task.FromResult(0); }
            });

        var exit = await app.RunAsync(new[] { "a", "then", "b" });
        Assert.Equal(7, exit);
        Assert.False(stage2Called);
    }
}
