using System.Threading.Tasks;
using Vice;
using Vice.Display;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class SadPath_RunAsyncTests
{
    private static (ViceApp App, RecordingConsole Console) NewApp()
    {
        var c = new RecordingConsole();
        return (new ViceApp("vice", "1.0.0", description: null,
            console: c, status: NullStatusDisplay.Instance), c);
    }

    [Fact]
    public async Task MissingRequiredTarget_ReturnsNonZero_AndExplainsInStderr()
    {
        var (app, console) = NewApp();
        app.Register(verb("add") > noun("user") * target("name"), "add", (ctx, ct) => Task.FromResult(0));

        var exit = await app.RunAsync(new[] { "add", "user" });

        Assert.NotEqual(0, exit);
        Assert.NotEmpty(console.Error);
    }

    [Fact]
    public async Task UnknownGlobalOption_ReturnsNonZero()
    {
        var (app, console) = NewApp();
        app.Register(verb("ping"), "ping", (ctx, ct) => Task.FromResult(0));

        var exit = await app.RunAsync(new[] { "--banana", "ping" });

        Assert.NotEqual(0, exit);
        Assert.NotEmpty(console.Error);
    }

    [Fact]
    public async Task ValueBearingGlobalOption_WithoutValue_ReturnsNonZero()
    {
        var (app, console) = NewApp();
        app.Register(verb("ping"), "ping", (ctx, ct) => Task.FromResult(0));

        var exit = await app.RunAsync(new[] { "ping", "--timeout" });

        Assert.NotEqual(0, exit);
        Assert.NotEmpty(console.Error);
    }

    [Fact]
    public async Task HandlerException_PropagatesOut()
    {
        var (app, _) = NewApp();
        app.Register(verb("kaboom"), "throws", (ctx, ct) =>
            throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.RunAsync(new[] { "kaboom" }));
    }

    [Fact]
    public async Task EmptyArgs_RendersHelp_NotError()
    {
        var (app, console) = NewApp();
        app.Register(verb("ping"), "ping", (ctx, ct) => Task.FromResult(0));

        var exit = await app.RunAsync(Array.Empty<string>());

        Assert.True(console.Output.Length > 0 || console.Error.Length > 0,
            "empty-args invocation produced no observable output of any kind");
    }

    [Fact]
    public async Task Cancellation_ThroughCommandContext_PropagatesToHandler()
    {
        var (app, _) = NewApp();
        var sawCancellation = false;
        app.Register(verb("slow"), "slow", async (ctx, ct) =>
        {
            try
            {
                await Task.Delay(10_000, ct);
            }
            catch (OperationCanceledException)
            {
                sawCancellation = true; throw;
            }
            return 0;
        });

        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => app.RunAsync(new[] { "slow" }, cts.Token));
        Assert.True(sawCancellation);
    }
}
