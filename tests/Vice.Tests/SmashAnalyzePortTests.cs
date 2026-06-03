using Vice.Display;
using Vice.Host;
using Vice.Options;
using Xunit;
using static Vice.Core.Dsl;

namespace Vice.Tests;

public class SmashAnalyzePortTests
{
    private sealed record AnalyzeCapture(bool Slowest, bool Failures, bool Flaky, string Format);

    private static (ViceApp App, AnalyzeCapture[] LastCall) BuildAnalyzeApp()
    {
        var slowest = Option.Flag("slowest", "Show slowest builds and tests", "s");
        var failures = Option.Flag("failures", "Show failure rates by subsystem", "f");
        var flaky = Option.Flag("flaky", "Detect flaky tests");
        var format = new Option<string>("format", "Output format (table|json|csv|plain)")
        {
            Aliases = new[] { "F" },
            Default = "table",
            Parser = s => s,
        };

        var last = new AnalyzeCapture[1];
        var app = new ViceApp("smash", "0.0.0", description: "analyze proof",
            console: new RecordingConsole(),
            status: NullStatusDisplay.Instance,
            globalOptions: new GlobalOption[] { slowest, failures, flaky, format });

        app.Register(verb("analyze"), "Analyze build health across subsystems", (ctx, ct) =>
        {
            last[0] = new AnalyzeCapture(
                ctx.Get(slowest),
                ctx.Get(failures),
                ctx.Get(flaky),
                ctx.Get(format) ?? "table");
            return Task.FromResult(0);
        });

        return (app, last);
    }

    [Fact]
    public async Task NoFlags_DefaultsApplied()
    {
        var (app, last) = BuildAnalyzeApp();
        await app.RunAsync(new[] { "analyze" });
        Assert.Equal(new AnalyzeCapture(false, false, false, "table"), last[0]);
    }

    [Fact]
    public async Task LongFlags_AllCapture()
    {
        var (app, last) = BuildAnalyzeApp();
        await app.RunAsync(new[] { "--slowest", "--failures", "--flaky", "analyze" });
        Assert.Equal(new AnalyzeCapture(true, true, true, "table"), last[0]);
    }

    [Fact]
    public async Task ShortAliases_AllCapture()
    {
        var (app, last) = BuildAnalyzeApp();
        await app.RunAsync(new[] { "-s", "-f", "analyze" });
        Assert.Equal(new AnalyzeCapture(true, true, false, "table"), last[0]);
    }

    [Fact]
    public async Task FormatEquals_Json()
    {
        var (app, last) = BuildAnalyzeApp();
        await app.RunAsync(new[] { "--format=json", "analyze" });
        Assert.Equal("json", last[0].Format);
    }

    [Fact]
    public async Task FormatShortAlias_Csv()
    {
        var (app, last) = BuildAnalyzeApp();
        await app.RunAsync(new[] { "-F", "csv", "analyze" });
        Assert.Equal("csv", last[0].Format);
    }

    [Fact]
    public async Task MixedShortAndLong()
    {
        var (app, last) = BuildAnalyzeApp();
        await app.RunAsync(new[] { "-s", "--format=json", "--flaky", "analyze" });
        Assert.Equal(new AnalyzeCapture(true, false, true, "json"), last[0]);
    }
}
