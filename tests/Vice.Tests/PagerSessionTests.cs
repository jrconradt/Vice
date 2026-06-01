using System.Threading.Tasks;
using Vice;
using Vice.Contracts;
using Vice.Display;
using Vice.Execution;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

[Collection("EnvVarSerial")]
public class PagerSessionTests
{
    private static ICommandContext FakeContext(bool noPager)
    {
        var c = new RecordingConsole();
        var app = new ViceApp("vice", "9.9.9", description: "test",
            console: c, status: NullStatusDisplay.Instance);
        ICommandContext? captured = null;
        app.Register(verb("probe"), "capture", (ctx, ct) =>
        {
            captured = ctx;
            return Task.FromResult(0);
        });
        var args = noPager ? new[] { "--no-pager", "probe" } : new[] { "probe" };
        app.RunAsync(args).GetAwaiter().GetResult();
        return captured!;
    }

    [Fact]
    public void NoPagerFlag_ReturnsDisabledSession()
    {
        var ctx = FakeContext(noPager: true);
        using var session = PagerSession.Open(ctx);
        Assert.False(session.IsActive);
        Assert.Equal(Console.Out, session.Writer);
    }

    [Fact]
    public void NonInteractiveOutput_ReturnsDisabledSession()
    {
        var ctx = FakeContext(noPager: false);
        using var session = PagerSession.Open(ctx);
        Assert.False(session.IsActive);
    }

    [Fact]
    public async Task Disabled_DisposeAsyncIsNoOp()
    {
        var ctx = FakeContext(noPager: true);
        var session = PagerSession.Open(ctx);
        await session.DisposeAsync();
    }
}
