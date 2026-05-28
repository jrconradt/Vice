using Vice.Display.Rendering;
using Xunit;

namespace Vice.Tests;

[Collection("EnvVarSerial")]
public class TerminalCapabilitiesColorTests
{
    [Fact]
    public void NoColor_DisablesEvenWhenForceColorSet()
    {
        using var env = new EnvScope(
            ("NO_COLOR", "1"),
            ("FORCE_COLOR", "1"),
            ("CLICOLOR_FORCE", "1"));

        var caps = TerminalCapabilities.Detect();

        Assert.False(caps.SupportsColor);
        Assert.Equal(ColorDepth.None, caps.ColorDepth);
    }

    [Fact]
    public void ForceColor_EnablesEvenWhenStdoutRedirected()
    {
        using var env = new EnvScope(
            ("NO_COLOR", null),
            ("FORCE_COLOR", "1"),
            ("CLICOLOR_FORCE", null));

        var caps = TerminalCapabilities.Detect();

        Assert.True(caps.SupportsColor);
        Assert.True(caps.SupportsAnsi);
        Assert.NotEqual(ColorDepth.None, caps.ColorDepth);
    }

    [Fact]
    public void CliColorForce_NonZero_Enables()
    {
        using var env = new EnvScope(
            ("NO_COLOR", null),
            ("FORCE_COLOR", null),
            ("CLICOLOR_FORCE", "1"));

        var caps = TerminalCapabilities.Detect();

        Assert.True(caps.SupportsColor);
    }

    [Fact]
    public void CliColorForce_Zero_DoesNotEnable()
    {
        using var env = new EnvScope(
            ("NO_COLOR", null),
            ("FORCE_COLOR", null),
            ("CLICOLOR_FORCE", "0"));

        var caps = TerminalCapabilities.Detect();

        Assert.False(caps.SupportsColor);
    }

    [Fact]
    public void WithForcedColor_PromotesNoneToTrueColor()
    {
        var off = TerminalCapabilities.None;
        var forced = off.WithForcedColor();

        Assert.True(forced.SupportsAnsi);
        Assert.True(forced.SupportsColor);
        Assert.Equal(ColorDepth.TrueColor, forced.ColorDepth);
    }

    [Fact]
    public void WithForcedColor_PreservesExistingDepth()
    {
        var caps = new TerminalCapabilities(
            supportsAnsi: true,
            supportsColor: true,
            colorDepth: ColorDepth.Color256,
            width: 80,
            isInteractive: true,
            supportsUnicode: true);

        var forced = caps.WithForcedColor();

        Assert.Equal(ColorDepth.Color256, forced.ColorDepth);
    }
}
