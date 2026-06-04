using Vice.Display;
using Vice.Host;
using Vice.Options;
using Xunit;
using static Vice.Core.Dsl;

namespace Vice.Tests;

public class TypedOptionsTests
{
    private static (ViceApp App, bool[] CapturedBool, string?[] CapturedString) Build(params GlobalOption[] options)
    {
        var capturedBool = new bool[1];
        var capturedString = new string?[1];
        var slowest = (Option<bool>)options[0];
        var format = options.Length > 1 ? (Option<string>)options[1] : null;

        var app = new ViceApp("vice", "9.9.9", description: "test app",
            console: new RecordingConsole(), status: NullStatusDisplay.Instance,
            globalOptions: options);

        app.Register(verb("probe"), "captures option state", (ctx, ct) =>
        {
            capturedBool[0] = ctx.Get(slowest);
            if (format is not null)
            {
                capturedString[0] = ctx.Get(format);
            }

            return Task.FromResult(0);
        });
        return (app, capturedBool, capturedString);
    }

    [Fact]
    public async Task Flag_LongForm_Captured()
    {
        var slowest = Option.Flag("slowest", "show slowest", "s");
        var (app, captured, _) = Build(slowest);

        await app.RunAsync(new[] { "--slowest", "probe" });

        Assert.True(captured[0]);
    }

    [Fact]
    public async Task Flag_ShortAlias_Captured()
    {
        var slowest = Option.Flag("slowest", "show slowest", "s");
        var (app, captured, _) = Build(slowest);

        await app.RunAsync(new[] { "-s", "probe" });

        Assert.True(captured[0]);
    }

    [Fact]
    public async Task Flag_NotProvided_DefaultsToFalse()
    {
        var slowest = Option.Flag("slowest", "show slowest", "s");
        var (app, captured, _) = Build(slowest);

        await app.RunAsync(new[] { "probe" });

        Assert.False(captured[0]);
    }

    [Fact]
    public async Task ValueBearing_StringOption_Parsed()
    {
        var slowest = Option.Flag("slowest", "show slowest", "s");
        var format = new Option<string>("format", "Output format")
        {
            Aliases = new[] { "f" },
            Default = "table",
            Parser = s => s,
        };
        var (app, _, captured) = Build(slowest, format);

        await app.RunAsync(new[] { "--format=json", "probe" });

        Assert.Equal("json", captured[0]);
    }

    [Fact]
    public async Task ValueBearing_ShortAliasWithSpace_Parsed()
    {
        var slowest = Option.Flag("slowest", "show slowest", "s");
        var format = new Option<string>("format", "Output format")
        {
            Aliases = new[] { "f" },
            Default = "table",
            Parser = s => s,
        };
        var (app, _, captured) = Build(slowest, format);

        await app.RunAsync(new[] { "-f", "json", "probe" });

        Assert.Equal("json", captured[0]);
    }

    [Fact]
    public async Task ValueBearing_NotProvided_ReturnsDefault()
    {
        var slowest = Option.Flag("slowest", "show slowest", "s");
        var format = new Option<string>("format", "Output format")
        {
            Default = "table",
            Parser = s => s,
        };
        var (app, _, captured) = Build(slowest, format);

        await app.RunAsync(new[] { "probe" });

        Assert.Equal("table", captured[0]);
    }
}
