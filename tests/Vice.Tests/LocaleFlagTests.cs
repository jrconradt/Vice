using System.Globalization;
using System.Threading.Tasks;
using Vice;
using Vice.Display;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

[Collection("EnvVarSerial")]
public class LocaleFlagTests
{
    [Fact]
    public async Task LocaleFlag_AppliesCulture_DuringHandlerInvocation()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo? captured = null;
            var c = new RecordingConsole();
            var app = new ViceApp("vice", "9.9.9", description: "test",
                console: c, status: NullStatusDisplay.Instance);
            app.Register(verb("probe"), "captures culture", (ctx, ct) =>
            {
                captured = CultureInfo.CurrentCulture;
                return Task.FromResult(0);
            });

            var exit = await app.RunAsync(new[] { "--locale", "de-DE", "probe" });

            Assert.Equal(0, exit);
            Assert.NotNull(captured);
            Assert.Equal("de-DE", captured!.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task LocaleFlag_ExposedOnContext()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            string? captured = null;
            var c = new RecordingConsole();
            var app = new ViceApp("vice", "9.9.9", description: "test",
                console: c, status: NullStatusDisplay.Instance);
            app.Register(verb("probe"), "captures locale", (ctx, ct) =>
            {
                captured = ctx.Locale;
                return Task.FromResult(0);
            });

            await app.RunAsync(new[] { "--locale", "fr-FR", "probe" });

            Assert.Equal("fr-FR", captured);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task LocaleFlag_InvalidLocaleTag_LogsErrorButContinues()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var c = new RecordingConsole();
            var app = new ViceApp("vice", "9.9.9", description: "test",
                console: c, status: NullStatusDisplay.Instance);
            app.Register(verb("probe"), "no-op", (ctx, ct) => Task.FromResult(0));

            var exit = await app.RunAsync(new[] { "--locale", "@@@invalid", "probe" });

            Assert.Equal(0, exit);
            Assert.Contains("Unknown locale", c.Error);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task NoLocaleFlag_LocalePropertyIsNull()
    {
        string? captured = "INITIAL";
        var c = new RecordingConsole();
        var app = new ViceApp("vice", "9.9.9", description: "test",
            console: c, status: NullStatusDisplay.Instance);
        app.Register(verb("probe"), "captures locale", (ctx, ct) =>
        {
            captured = ctx.Locale;
            return Task.FromResult(0);
        });

        await app.RunAsync(new[] { "probe" });

        Assert.Null(captured);
    }
}
