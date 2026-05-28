using System.Threading.Tasks;
using Vice;
using Vice.Execution;
using Vice.Display;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class BehaviorFlagsTests
{
    private static (ViceApp App, RecordingConsole Console, int[] CapturedFlags) Build()
    {
        var c = new RecordingConsole();
        var captured = new int[5];
        var app = new ViceApp("vice", "9.9.9", description: "test app",
            console: c, status: NullStatusDisplay.Instance);
        app.Register(verb("probe"), "captures flag state", (ctx, ct) =>
        {
            captured[0] = ctx.Verbose ? 1 : 0;
            captured[1] = ctx.Quiet ? 1 : 0;
            captured[2] = ctx.DryRun ? 1 : 0;
            captured[3] = ctx.NonInteractive ? 1 : 0;
            captured[4] = ctx.NoPager ? 1 : 0;
            return Task.FromResult(0);
        });
        return (app, c, captured);
    }

    [Fact]
    public async Task NoFlags_AllFalse()
    {
        var (app, _, flags) = Build();

        await app.RunAsync(new[] { "probe" });

        Assert.Equal(new[] { 0, 0, 0, 0, 0 }, flags);
    }

    [Fact]
    public async Task NoPager_SetsFlag()
    {
        var (app, _, flags) = Build();
        await app.RunAsync(new[] { "--no-pager", "probe" });
        Assert.Equal(1, flags[4]);
    }

    [Fact]
    public async Task Verbose_SetsFlag()
    {
        var (app, _, flags) = Build();
        await app.RunAsync(new[] { "--verbose", "probe" });
        Assert.Equal(1, flags[0]);
    }

    [Fact]
    public async Task Quiet_SetsFlag()
    {
        var (app, _, flags) = Build();
        await app.RunAsync(new[] { "--quiet", "probe" });
        Assert.Equal(1, flags[1]);
    }

    [Fact]
    public async Task DryRun_SetsFlag()
    {
        var (app, _, flags) = Build();
        await app.RunAsync(new[] { "--dry-run", "probe" });
        Assert.Equal(1, flags[2]);
    }

    [Fact]
    public async Task NonInteractive_SetsFlag()
    {
        var (app, _, flags) = Build();
        await app.RunAsync(new[] { "--non-interactive", "probe" });
        Assert.Equal(1, flags[3]);
    }

    [Fact]
    public async Task AllFlags_SetTogether()
    {
        var (app, _, flags) = Build();
        await app.RunAsync(new[] { "--verbose", "--quiet", "--dry-run", "--non-interactive", "--no-pager", "probe" });
        Assert.Equal(new[] { 1, 1, 1, 1, 1 }, flags);
    }
}
