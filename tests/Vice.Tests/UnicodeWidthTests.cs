using Vice.Display.Rendering;
using Xunit;

namespace Vice.Tests;

public class UnicodeWidthTests
{
    [Fact]
    public void GetWidth_Empty_IsZero()
    {
        Assert.Equal(0, UnicodeWidth.GetWidth(""));
    }

    [Fact]
    public void GetWidth_Ascii_IsLength()
    {
        Assert.Equal(5, UnicodeWidth.GetWidth("hello"));
    }

    [Fact]
    public void GetWidth_CjkWide_IsTwoPerCharacter()
    {
        Assert.Equal(4, UnicodeWidth.GetWidth("中文"));
    }

    [Fact]
    public void GetWidth_HangulSyllable_IsTwo()
    {
        Assert.Equal(2, UnicodeWidth.GetWidth("가"));
    }

    [Fact]
    public void GetWidth_FullwidthForm_IsTwo()
    {
        Assert.Equal(2, UnicodeWidth.GetWidth("Ａ"));
    }

    [Fact]
    public void GetWidth_CombiningMark_IsZeroWidth()
    {
        Assert.Equal(1, UnicodeWidth.GetWidth("é"));
    }

    [Fact]
    public void GetWidth_ZeroWidthJoiner_IsZero()
    {
        Assert.Equal(1, UnicodeWidth.GetWidth("a‍"));
    }

    [Fact]
    public void GetWidth_EmojiWithVariationSelector16_IsTwo()
    {
        Assert.Equal(2, UnicodeWidth.GetWidth("❤️"));
    }

    [Fact]
    public void GetWidth_AstralEmojiSurrogatePair_IsTwo()
    {
        Assert.Equal(2, UnicodeWidth.GetWidth("😀"));
    }

    [Fact]
    public void GetWidth_AstralCjkExtensionSurrogatePair_IsTwo()
    {
        Assert.Equal(2, UnicodeWidth.GetWidth("𠀀"));
    }

    [Fact]
    public void PadRight_ShorterText_PadsToWidth()
    {
        Assert.Equal("ab   ", UnicodeWidth.PadRight("ab", 5));
    }

    [Fact]
    public void PadRight_WideText_AccountsForDoubleWidth()
    {
        Assert.Equal("中 ", UnicodeWidth.PadRight("中", 3));
    }

    [Fact]
    public void PadRight_AlreadyWide_ReturnsUnchanged()
    {
        Assert.Equal("abcdef", UnicodeWidth.PadRight("abcdef", 4));
    }

    [Fact]
    public void Truncate_TextWithinWidth_ReturnsUnchanged()
    {
        Assert.Equal("hello", UnicodeWidth.Truncate("hello", 10));
    }

    [Fact]
    public void Truncate_LongText_AppendsEllipsis()
    {
        Assert.Equal("hel...", UnicodeWidth.Truncate("helloworld", 6));
    }

    [Fact]
    public void Truncate_MaxWidthSmallerThanEllipsis_ReturnsClippedEllipsis()
    {
        Assert.Equal("..", UnicodeWidth.Truncate("helloworld", 2));
    }

    [Fact]
    public void Truncate_MaxWidthZero_ReturnsEmpty()
    {
        Assert.Equal("", UnicodeWidth.Truncate("helloworld", 0));
    }

    [Fact]
    public void Truncate_WideText_StopsBeforeOverflowingTarget()
    {
        Assert.Equal("中...", UnicodeWidth.Truncate("中文語", 5));
    }
}
