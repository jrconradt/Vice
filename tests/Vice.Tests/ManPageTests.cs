using Vice.Display;
using Vice.TestSupport;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class ManPageTests
{
    private static (ViceApp App, RecordingConsole Console) Build(string appName = "vice")
    {
        var c = new RecordingConsole();
        var app = new ViceApp(appName, "9.9.9", description: "test app",
            console: c, status: NullStatusDisplay.Instance);
        app.Register(verb("alpha"), "alpha description", (ctx, ct) => Task.FromResult(0));
        app.Register(verb("beta") > noun("things") * target("path"), "beta description", (ctx, ct) => Task.FromResult(0));
        return (app, c);
    }

    [Fact]
    public async Task EmitsManPageHeaders()
    {
        var (app, console) = Build();
        var exit = await app.RunAsync(new[] { "manpage" });

        Assert.Equal(0, exit);
        Assert.Contains(".TH VICE 1", console.Output);
        Assert.Contains(".SH NAME", console.Output);
        Assert.Contains(".SH SYNOPSIS", console.Output);
        Assert.Contains(".SH DESCRIPTION", console.Output);
        Assert.Contains(".SH COMMANDS", console.Output);
        Assert.Contains(".SH GLOBAL OPTIONS", console.Output);
        Assert.Contains(".SH EXIT STATUS", console.Output);
        Assert.Contains(".SH ENVIRONMENT", console.Output);
        Assert.Contains(".SH SEE ALSO", console.Output);
    }

    [Fact]
    public async Task IncludesEveryRegisteredVerb()
    {
        var (app, console) = Build();
        await app.RunAsync(new[] { "manpage" });

        Assert.Contains("alpha", console.Output);
        Assert.Contains("alpha description", console.Output);
        Assert.Contains("beta", console.Output);
        Assert.Contains("beta description", console.Output);
    }

    [Fact]
    public async Task IncludesGlobalOptions()
    {
        var (app, console) = Build();
        await app.RunAsync(new[] { "manpage" });

        Assert.Contains("\\-\\-help", console.Output);
        Assert.Contains("\\-\\-format", console.Output);
        Assert.Contains("\\-\\-no\\-color", console.Output);
    }

    [Fact]
    public async Task IncludesExitCodes()
    {
        var (app, console) = Build();
        await app.RunAsync(new[] { "manpage" });

        Assert.Contains(".B 0", console.Output);
        Assert.Contains(".B 1", console.Output);
        Assert.Contains(".B 2", console.Output);
        Assert.Contains(".B 130", console.Output);
    }

    [Fact]
    public async Task AppNameAppearsInTitleAndCapitalized()
    {
        var (app, console) = Build("chain-asm");
        await app.RunAsync(new[] { "manpage" });

        Assert.Contains(".TH CHAIN\\-ASM 1", console.Output);
        Assert.Contains("chain\\-asm 9.9.9", console.Output);
        Assert.Contains("CHAIN-ASM_LOG_LEVEL", console.Output);
    }

    [Fact]
    public async Task Manpage_renders_optional_with_brackets()
    {
        var c = new RecordingConsole();
        var app = new ViceApp("vice", "9.9.9", description: "test app",
            console: c, status: NullStatusDisplay.Instance);
        app.Register(verb("foo") > optional(noun("to") * target("path")), "foo desc", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "manpage" });

        Assert.Contains("foo [to {path}]", c.Output);
    }

    [Fact]
    public async Task Manpage_renders_alternation_with_parens_and_pipes()
    {
        var c = new RecordingConsole();
        var app = new ViceApp("vice", "9.9.9", description: "test app",
            console: c, status: NullStatusDisplay.Instance);
        app.Register(verb("send") > oneOf(verb("data"), verb("file") * target("path")), "send desc", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "manpage" });

        Assert.Contains("send (data|file {path})", c.Output);
    }

    [Fact]
    public async Task Manpage_renders_repetition_with_ellipsis_no_separator()
    {
        var c = new RecordingConsole();
        var app = new ViceApp("vice", "9.9.9", description: "test app",
            console: c, status: NullStatusDisplay.Instance);
        app.Register(verb("scan") > repeat(verb("path"), min: 1), "scan desc", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "manpage" });

        Assert.Contains("path...", c.Output);
    }

    [Fact]
    public async Task Manpage_renders_repetition_with_separator()
    {
        var c = new RecordingConsole();
        var app = new ViceApp("vice", "9.9.9", description: "test app",
            console: c, status: NullStatusDisplay.Instance);
        app.Register(verb("filter") > repeat(noun("by") * target("axis"), min: 1, separator: verb("and")), "filter desc", (ctx, ct) => Task.FromResult(0));

        await app.RunAsync(new[] { "manpage" });

        Assert.Contains("by {axis}", c.Output);
        Assert.Contains("[and by {axis}...]", c.Output);
    }

    [Fact]
    public async Task FullManPageMatchesGolden()
    {
        var (app, console) = Build();
        var exit = await app.RunAsync(new[] { "manpage" });

        Assert.Equal(0, exit);
        GoldenFile.Verify("manpage.golden", console.Output);
    }
}
