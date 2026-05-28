using System.Threading.Tasks;
using Vice;
using Vice.Execution;
using Vice.Display;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class OutputFormatTests
{
    private static (ViceApp App, RecordingConsole Console, OutputFormatKind[] Format, bool[] WantsJson) Build()
    {
        var c = new RecordingConsole();
        var format = new OutputFormatKind[1];
        var wantsJson = new bool[1];
        var app = new ViceApp("vice", "9.9.9", description: "test app",
            console: c, status: NullStatusDisplay.Instance);
        app.Register(verb("probe"), "captures format", (ctx, ct) =>
        {
            format[0] = ctx.OutputFormat;
            wantsJson[0] = ctx.WantsJson;
            return Task.FromResult(0);
        });
        return (app, c, format, wantsJson);
    }

    [Fact]
    public async Task NoFlag_DefaultsToAuto()
    {
        var (app, _, format, wantsJson) = Build();
        await app.RunAsync(new[] { "probe" });
        Assert.Equal(OutputFormatKind.Auto, format[0]);
        Assert.False(wantsJson[0]);
    }

    [Fact]
    public async Task FormatJson_SetsWantsJson()
    {
        var (app, _, format, wantsJson) = Build();
        await app.RunAsync(new[] { "--format", "json", "probe" });
        Assert.Equal(OutputFormatKind.Json, format[0]);
        Assert.True(wantsJson[0]);
    }

    [Fact]
    public async Task FormatJsonl_AlsoSetsWantsJson()
    {
        var (app, _, format, wantsJson) = Build();
        await app.RunAsync(new[] { "--format", "jsonl", "probe" });
        Assert.Equal(OutputFormatKind.Jsonl, format[0]);
        Assert.True(wantsJson[0]);
    }

    [Fact]
    public async Task FormatText_DoesNotSetWantsJson()
    {
        var (app, _, format, wantsJson) = Build();
        await app.RunAsync(new[] { "--format", "text", "probe" });
        Assert.Equal(OutputFormatKind.Text, format[0]);
        Assert.False(wantsJson[0]);
    }

    [Fact]
    public async Task FormatNormalizedToLowercase()
    {
        var (app, _, format, wantsJson) = Build();
        await app.RunAsync(new[] { "--format", "JSON", "probe" });
        Assert.Equal(OutputFormatKind.Json, format[0]);
        Assert.True(wantsJson[0]);
    }
}
