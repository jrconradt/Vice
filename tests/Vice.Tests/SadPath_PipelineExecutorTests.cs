using System.Threading.Tasks;
using Vice;
using Vice.Lexicon;
using Vice.Display;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class SadPath_PipelineExecutorTests
{
    private static ViceApp NewApp() => new ViceApp("vice", "1.0.0", description: null,
        console: new RecordingConsole(), status: NullStatusDisplay.Instance);

    [Fact]
    public async Task Stage1_Throws_PropagatesException()
    {
        var app = NewApp();
        app.RegisterPipeline(
            verb("a") > Connectors.Then() > noun("b"), "p",
            (ctx, ct) => Task.FromResult(0),
            new()
            {
                [0] = (ctx, ct) => throw new InvalidOperationException("stage1"),
                [1] = (ctx, ct) => Task.FromResult(0),
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.RunAsync(new[] { "a", "then", "b" }));
    }

    [Fact]
    public async Task And_Stage2Failure_PropagatesExitCode()
    {
        var app = NewApp();
        app.RegisterPipeline(
            verb("a") > Connectors.AndPipe() > noun("b"), "ap",
            (ctx, ct) => Task.FromResult(0),
            new()
            {
                [0] = (ctx, ct) => Task.FromResult(0),
                [1] = (ctx, ct) => Task.FromResult(9),
            });

        Assert.Equal(9, await app.RunAsync(new[] { "a", "and", "b" }));
    }

    [Fact]
    public async Task Or_BothStagesFail_PropagatesSecondExitCode()
    {
        var app = NewApp();
        app.RegisterPipeline(
            verb("a") > Connectors.Or() > noun("b"), "op",
            (ctx, ct) => Task.FromResult(0),
            new()
            {
                [0] = (ctx, ct) => Task.FromResult(1),
                [1] = (ctx, ct) => Task.FromResult(2),
            });

        Assert.Equal(2, await app.RunAsync(new[] { "a", "or", "b" }));
    }

    [Fact]
    public async Task Then_PreservesNonZeroFromFirstStage_WhenSecondNeverRuns()
    {
        var app = NewApp();
        var secondRan = false;
        app.RegisterPipeline(
            verb("a") > Connectors.Then() > noun("b"), "tp",
            (ctx, ct) => Task.FromResult(0),
            new()
            {
                [0] = (ctx, ct) => Task.FromResult(42),
                [1] = (ctx, ct) => { secondRan = true; return Task.FromResult(0); },
            });

        Assert.Equal(42, await app.RunAsync(new[] { "a", "then", "b" }));
        Assert.False(secondRan);
    }
}
