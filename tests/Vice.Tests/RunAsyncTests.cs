using System.Threading.Tasks;
using Vice.Core;
using Vice.Display;
using Vice.Host;
using Vice.Options;
using Xunit;
using static Vice.Core.Dsl;

namespace Vice.Tests;

public class RunAsyncTests
{
    private static (ViceApp App, RecordingConsole Console) NewApp() =>
        NewAppWith();

    private static (ViceApp App, RecordingConsole Console) NewAppWith(string name = "vice", string version = "1.0.0")
    {
        var console = new RecordingConsole();
        var app = new ViceApp(name, version, description: null,
            console: console, status: NullStatusDisplay.Instance);
        return (app, console);
    }

    [Fact]
    public async Task SingleVerb_InvokesHandler()
    {
        var (app, _) = NewApp();
        var called = false;
        app.Register(verb("ping"), "ping", (ctx, ct) => { called = true; return Task.FromResult(0); });

        Assert.Equal(0, await app.RunAsync(new[] { "ping" }));
        Assert.True(called);
    }

    [Fact]
    public async Task Target_BindsValue()
    {
        var (app, _) = NewApp();
        string? bound = null;
        app.Register(verb("add") > noun("user") * target("name"), "add user", (ctx, ct) =>
        {
            bound = ctx["name"];
            return Task.FromResult(0);
        });

        Assert.Equal(0, await app.RunAsync(new[] { "add", "user", "alice" }));
        Assert.Equal("alice", bound);
    }

    [Fact]
    public async Task Synonym_Resolves()
    {
        var (app, _) = NewApp();
        var called = false;
        app.Register(verb("add", "create"), "add", (ctx, ct) => { called = true; return Task.FromResult(0); });

        Assert.Equal(0, await app.RunAsync(new[] { "create" }));
        Assert.True(called);
    }

    [Fact]
    public async Task Unknown_ReturnsNonZero_AndWritesError()
    {
        var (app, console) = NewApp();
        var exit = await app.RunAsync(new[] { "no-such-verb" });
        Assert.NotEqual(0, exit);
        Assert.NotEmpty(console.Error);
    }

    [Fact]
    public async Task HelpFlag_ReturnsZero_AndRendersTitle()
    {
        var (app, console) = NewApp();
        app.Register(verb("ping"), "ping desc", (ctx, ct) => Task.FromResult(0));

        Assert.Equal(0, await app.RunAsync(new[] { "--help" }));
        Assert.Contains("vice", console.Output);
        Assert.Contains("1.0.0", console.Output);
    }

    [Fact]
    public async Task VersionFlag_PrintsNameAndVersion()
    {
        var (app, console) = NewApp();
        Assert.Equal(0, await app.RunAsync(new[] { "--version" }));
        Assert.Contains("vice", console.Output);
        Assert.Contains("1.0.0", console.Output);
    }

    [Fact]
    public async Task CustomGlobalOption_IsRecognized()
    {
        var opt = new ValueBearingOption("scope", "scope filter");
        var console = new RecordingConsole();
        var app = new ViceApp("vice", "1.0.0", description: null,
            console: console, status: NullStatusDisplay.Instance,
            globalOptions: new[] { (GlobalOption)opt });

        string? observed = null;
        app.Register(verb("list"), "list", (ctx, ct) =>
        {
            observed = ctx.GetGlobalOption(opt);
            return Task.FromResult(0);
        });

        Assert.Equal(0, await app.RunAsync(new[] { "--scope", "active", "list" }));
        Assert.Equal("active", observed);
    }

    [Fact]
    public async Task HandlerExitCode_Propagates()
    {
        var (app, _) = NewApp();
        app.Register(verb("fail"), "fail", (ctx, ct) => Task.FromResult(42));
        Assert.Equal(42, await app.RunAsync(new[] { "fail" }));
    }

    [Fact]
    public void RegisteredGlobalOptions_IncludesFramework()
    {
        var (app, _) = NewApp();
        var names = app.RegisteredGlobalOptions.Select(o => o.Name).ToHashSet();
        Assert.Contains("help", names);
        Assert.Contains("version", names);
        Assert.Contains("no-status", names);
    }

    [Fact]
    public async Task Verbose_ReachesHandler()
    {
        var (app, _) = NewApp();
        bool? sawVerbose = null;
        app.Register(verb("vp"), "verbose probe", (ctx, ct) =>
        {
            sawVerbose = ctx.Verbose;
            return Task.FromResult(0);
        });

        await app.RunAsync(new[] { "--verbose", "vp" });
        Assert.True(sawVerbose);
    }
}
