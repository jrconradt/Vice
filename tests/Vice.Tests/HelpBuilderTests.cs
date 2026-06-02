using Vice.Display;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class HelpBuilderTests
{
    private static (ViceApp App, RecordingConsole Console) Build()
    {
        var c = new RecordingConsole();
        return (new ViceApp("vice", "9.9.9", description: "test app",
            console: c, status: NullStatusDisplay.Instance), c);
    }

    [Fact]
    public async Task Help_ListsRegisteredCommands()
    {
        var (app, console) = Build();
        app.Register(verb("alpha"), "alpha description", (ctx, ct) => Task.FromResult(0));
        app.Register(verb("beta"), "beta description", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "--help" });

        Assert.Contains("alpha", console.Output);
        Assert.Contains("alpha description", console.Output);
        Assert.Contains("beta", console.Output);
    }

    [Fact]
    public async Task Help_OmitsHiddenCommands()
    {
        var (app, console) = Build();
        app.Register(verb("visible"), "visible", (ctx, ct) => Task.FromResult(0));
        app.Register(verb("secret"), "secret", (ctx, ct) => Task.FromResult(0), showInHelp: false);

        await app.RunAsync(new[] { "--help" });

        Assert.Contains("visible", console.Output);
        Assert.DoesNotContain("secret description", console.Output);

        var line = console.Output.Split('\n').FirstOrDefault(l => l.Contains("secret"));
        Assert.Null(line);
    }

    [Fact]
    public async Task Help_ShowsAppTitleNameAndVersion()
    {
        var (app, console) = Build();
        await app.RunAsync(new[] { "--help" });
        Assert.Contains("vice", console.Output);
        Assert.Contains("9.9.9", console.Output);
        Assert.Contains("test app", console.Output);
    }

    [Fact]
    public async Task Help_RendersGlobalOptions()
    {
        var (app, console) = Build();
        await app.RunAsync(new[] { "--help" });
        Assert.Contains("--help", console.Output);
        Assert.Contains("--version", console.Output);
        Assert.Contains("--verbose", console.Output);
    }

    [Fact]
    public async Task Help_renders_optional_with_brackets()
    {
        var (app, console) = Build();
        app.Register(verb("foo") > optional(noun("to") * target("path")), "foo desc", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "--help" });

        Assert.Contains("foo [to {path}]", console.Output);
    }

    [Fact]
    public async Task Help_renders_alternation_with_parens_and_pipes()
    {
        var (app, console) = Build();
        app.Register(verb("send") > oneOf(verb("data"), verb("file") * target("path")), "send desc", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "--help" });

        Assert.Contains("send (data|file {path})", console.Output);
    }

    [Fact]
    public async Task Help_renders_repetition_with_ellipsis_no_separator()
    {
        var (app, console) = Build();
        app.Register(verb("scan") > repeat(verb("path"), min: 1), "scan desc", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "--help" });

        Assert.Contains("path...", console.Output);
    }

    [Fact]
    public async Task Help_renders_repetition_with_separator()
    {
        var (app, console) = Build();
        app.Register(verb("filter") > repeat(noun("by") * target("axis"), min: 1, separator: verb("and")), "filter desc", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "--help" });

        Assert.Contains("by {axis}", console.Output);
        Assert.Contains("[and by {axis}...]", console.Output);
    }

    [Fact]
    public async Task Help_FullLayoutMatchesGolden()
    {
        var (app, console) = Build();
        app.Register(verb("alpha", "a"),
                     "alpha description",
                     (ctx, ct) => Task.FromResult(0));
        app.Register(verb("beta") > noun("things") * target("path"),
                     "beta description",
                     (ctx, ct) => Task.FromResult(0));
        app.Register(verb("gamma"),
                     "gamma description",
                     (ctx, ct) => Task.FromResult(0),
                     showInHelp: false);

        var exit = await app.RunAsync(new[] { "--help" });

        Assert.Equal(0, exit);
        GoldenFile.Verify("help_full.golden", console.Output);
    }
}
