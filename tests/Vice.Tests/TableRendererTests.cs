using Vice.Display.Rendering;
using Xunit;

namespace Vice.Tests;

public class TableRendererTests
{
    private static TerminalCapabilities Caps(
        int width,
        bool supportsColor,
        ColorDepth colorDepth,
        bool supportsUnicode)
    {
        return new TerminalCapabilities(
            supportsAnsi: supportsColor,
            supportsColor: supportsColor,
            colorDepth: colorDepth,
            width: width,
            isInteractive: true,
            supportsUnicode: supportsUnicode);
    }

    [Fact]
    public void Render_ZeroColumns_ReturnsEmpty()
    {
        var lines = TableRenderer.Render(
            Array.Empty<TableColumn>(),
            Array.Empty<string[]>(),
            TableBorder.Rounded,
            null,
            Caps(80, false, ColorDepth.None, true));

        Assert.Empty(lines);
    }

    [Fact]
    public void Render_NoBorder_SeparatesColumnsWithTwoSpaces()
    {
        var columns = new[]
        {
            new TableColumn("A"),
            new TableColumn("B")
        };
        var rows = new List<string[]>
        {
            new[] { "x", "y" }
        };

        var lines = TableRenderer.Render(
            columns,
            rows,
            TableBorder.None,
            null,
            Caps(80, false, ColorDepth.None, true));

        Assert.Equal(2, lines.Count);
        Assert.Equal("A  B", lines[0]);
        Assert.Equal("x  y", lines[1]);
    }

    [Fact]
    public void Render_Bordered_EmitsTopHeaderAndBottomLines()
    {
        var columns = new[]
        {
            new TableColumn("H")
        };
        var rows = new List<string[]>
        {
            new[] { "v" }
        };

        var lines = TableRenderer.Render(
            columns,
            rows,
            TableBorder.Ascii,
            null,
            Caps(80, false, ColorDepth.None, false));

        Assert.Equal(4, lines.Count);
        Assert.Equal("+---+", lines[0]);
        Assert.Equal("| H |", lines[1]);
        Assert.Equal("+---+", lines[2]);
        Assert.Equal("| v |", lines[3]);
    }

    [Fact]
    public void Render_NonUnicodeTerminal_DowngradesRoundedToAscii()
    {
        var columns = new[]
        {
            new TableColumn("H")
        };

        var lines = TableRenderer.Render(
            columns,
            new List<string[]>(),
            TableBorder.Rounded,
            null,
            Caps(80, false, ColorDepth.None, false));

        Assert.Equal("+---+", lines[0]);
    }

    [Fact]
    public void Render_ContentExceedsAvailableWidth_ShrinksColumns()
    {
        var columns = new[]
        {
            new TableColumn("aaaaaaaaaa"),
            new TableColumn("bbbbbbbbbb")
        };
        var rows = new List<string[]>
        {
            new[] { "aaaaaaaaaa", "bbbbbbbbbb" }
        };

        var lines = TableRenderer.Render(
            columns,
            rows,
            TableBorder.None,
            null,
            Caps(12, false, ColorDepth.None, true));

        foreach (var line in lines)
        {
            Assert.True(UnicodeWidth.GetWidth(line) <= 12);
        }
    }

    [Fact]
    public void Render_WideCharContentExceedsMaxWidth_IsTruncated()
    {
        var columns = new[]
        {
            new TableColumn("col")
            {
                MaxWidth = 5
            }
        };
        var rows = new List<string[]>
        {
            new[] { "中文語句子" }
        };

        var lines = TableRenderer.Render(
            columns,
            rows,
            TableBorder.None,
            null,
            Caps(80, false, ColorDepth.None, true));

        Assert.Equal(2, lines.Count);
        Assert.Equal("中...", lines[1]);
    }

    [Fact]
    public void Render_LeftAlignment_PadsRight()
    {
        var columns = new[]
        {
            new TableColumn("header")
            {
                Alignment = Alignment.Left
            }
        };
        var rows = new List<string[]>
        {
            new[] { "x" }
        };

        var lines = TableRenderer.Render(
            columns,
            rows,
            TableBorder.None,
            null,
            Caps(80, false, ColorDepth.None, true));

        Assert.Equal("x     ", lines[1]);
    }

    [Fact]
    public void Render_RightAlignment_PadsLeft()
    {
        var columns = new[]
        {
            new TableColumn("header")
            {
                Alignment = Alignment.Right
            }
        };
        var rows = new List<string[]>
        {
            new[] { "x" }
        };

        var lines = TableRenderer.Render(
            columns,
            rows,
            TableBorder.None,
            null,
            Caps(80, false, ColorDepth.None, true));

        Assert.Equal("     x", lines[1]);
    }

    [Fact]
    public void Render_CenterAlignment_SplitsPadding()
    {
        var columns = new[]
        {
            new TableColumn("header")
            {
                Alignment = Alignment.Center
            }
        };
        var rows = new List<string[]>
        {
            new[] { "x" }
        };

        var lines = TableRenderer.Render(
            columns,
            rows,
            TableBorder.None,
            null,
            Caps(80, false, ColorDepth.None, true));

        Assert.Equal("  x   ", lines[1]);
    }

    [Fact]
    public void Render_WithColorAndStyle_WrapsCellInAnsiButStripsToContent()
    {
        var columns = new[]
        {
            new TableColumn("H")
            {
                CellStyle = Style.Default.Fg(Color.Red)
            }
        };
        var rows = new List<string[]>
        {
            new[] { "v" }
        };

        var lines = TableRenderer.Render(
            columns,
            rows,
            TableBorder.None,
            null,
            Caps(80, true, ColorDepth.TrueColor, true));

        Assert.Contains("[", lines[1]);
        Assert.Equal("v", AnsiStripper.Strip(lines[1]));
    }

    [Fact]
    public void Render_ColorDepthNone_EmitsNoAnsi()
    {
        var columns = new[]
        {
            new TableColumn("H")
            {
                CellStyle = Style.Default.Fg(Color.Red)
            }
        };
        var rows = new List<string[]>
        {
            new[] { "v" }
        };

        var lines = TableRenderer.Render(
            columns,
            rows,
            TableBorder.None,
            null,
            Caps(80, false, ColorDepth.None, true));

        Assert.DoesNotContain("[", lines[1]);
    }

    [Fact]
    public void ToAnsiFg_BasicColor_UsesStandardForegroundCode()
    {
        Assert.Equal("[31m", Color.Red.ToAnsiFg(ColorDepth.Basic8));
    }

    [Fact]
    public void ToAnsiFg_BrightColor_UsesHighIntensityCode()
    {
        Assert.Equal("[91m", Color.BrightRed.ToAnsiFg(ColorDepth.Basic8));
    }

    [Fact]
    public void ToAnsiBg_BasicColor_UsesStandardBackgroundCode()
    {
        Assert.Equal("[41m", Color.Red.ToAnsiBg(ColorDepth.Basic8));
    }

    [Fact]
    public void ToAnsiFg_None_EmitsEmpty()
    {
        Assert.Equal("", Color.From256(200).ToAnsiFg(ColorDepth.None));
        Assert.Equal("", Color.Rgb(10, 20, 30).ToAnsiFg(ColorDepth.None));
    }

    [Fact]
    public void ToAnsiFg_Color256_AtColor256Depth_EmitsIndexedCode()
    {
        Assert.Equal("[38;5;200m", Color.From256(200).ToAnsiFg(ColorDepth.Color256));
    }

    [Fact]
    public void ToAnsiBg_Color256_AtColor256Depth_EmitsIndexedCode()
    {
        Assert.Equal("[48;5;200m", Color.From256(200).ToAnsiBg(ColorDepth.Color256));
    }

    [Fact]
    public void ToAnsiFg_Color256_AtBasicDepth_DowngradesToBasicRange()
    {
        var code = Color.From256(200).ToAnsiFg(ColorDepth.Basic8);
        Assert.Matches(@"^\[(3[0-7]|9[0-7])m$", code);
    }

    [Fact]
    public void ToAnsiFg_Rgb_AtTrueColor_EmitsTrueColorCode()
    {
        Assert.Equal("[38;2;255;0;0m", Color.Rgb(255, 0, 0).ToAnsiFg(ColorDepth.TrueColor));
    }

    [Fact]
    public void ToAnsiBg_Rgb_AtTrueColor_EmitsTrueColorCode()
    {
        Assert.Equal("[48;2;0;128;255m", Color.Rgb(0, 128, 255).ToAnsiBg(ColorDepth.TrueColor));
    }

    [Fact]
    public void ToAnsiFg_Rgb_AtColor256_EmitsIndexedCode()
    {
        Assert.Equal("[38;5;196m", Color.Rgb(255, 0, 0).ToAnsiFg(ColorDepth.Color256));
    }

    [Fact]
    public void ToAnsiFg_Rgb_AtBasicDepth_DowngradesToBasicRange()
    {
        var code = Color.Rgb(255, 0, 0).ToAnsiFg(ColorDepth.Basic8);
        Assert.Matches(@"^\[(3[0-7]|9[0-7])m$", code);
    }
}
