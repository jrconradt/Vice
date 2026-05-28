using Xunit;

namespace Vice.Parser.Tests;

public class TypedOptionsTests
{
    [Fact]
    public void Lexer_ShortOption_ProducesGlobalOptionToken()
    {
        var tokens = Lexer.Tokenize(["-s"]);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.GlobalOption, tokens[0].Kind);
        Assert.Equal("s", tokens[0].Value);
    }

    [Fact]
    public void Lexer_LongOptionWithEquals_SplitsIntoTwoTokens()
    {
        var tokens = Lexer.Tokenize(["--format=json"]);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.GlobalOption, tokens[0].Kind);
        Assert.Equal("format", tokens[0].Value);
        Assert.Equal(TokenKind.Word, tokens[1].Kind);
        Assert.Equal("json", tokens[1].Value);
    }

    [Fact]
    public void Lexer_ShortOptionWithEquals_SplitsIntoTwoTokens()
    {
        var tokens = Lexer.Tokenize(["-f=json"]);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.GlobalOption, tokens[0].Kind);
        Assert.Equal("f", tokens[0].Value);
        Assert.Equal("json", tokens[1].Value);
    }

    [Fact]
    public void Lexer_NegativeNumber_StaysWord()
    {
        var tokens = Lexer.Tokenize(["-1"]);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Word, tokens[0].Kind);
        Assert.Equal("-1", tokens[0].Value);
    }

    [Fact]
    public void Extractor_ResolvesAliasToCanonical()
    {
        var registry = new OptionRegistry();
        registry.Add(new OptionMetadata("slowest", new[] { "s" }, TakesValue: false, Required: false));

        var tokens = Lexer.Tokenize(["-s"]);
        var (_, globals, errors) = GlobalOptionExtractor.Extract(tokens, registry);

        Assert.Empty(errors);
        Assert.True(globals.ContainsKey("slowest"));
        Assert.False(globals.ContainsKey("s"));
    }

    [Fact]
    public void Extractor_ValueBearingOption_CapturesValue()
    {
        var registry = new OptionRegistry();
        registry.Add(new OptionMetadata("format", Array.Empty<string>(), TakesValue: true, Required: false));

        var tokens = Lexer.Tokenize(["--format", "json"]);
        var (_, globals, errors) = GlobalOptionExtractor.Extract(tokens, registry);

        Assert.Empty(errors);
        Assert.Equal("json", globals["format"]);
    }

    [Fact]
    public void Extractor_ValueBearingOption_CapturesEqualsValue()
    {
        var registry = new OptionRegistry();
        registry.Add(new OptionMetadata("format", Array.Empty<string>(), TakesValue: true, Required: false));

        var tokens = Lexer.Tokenize(["--format=json"]);
        var (_, globals, errors) = GlobalOptionExtractor.Extract(tokens, registry);

        Assert.Empty(errors);
        Assert.Equal("json", globals["format"]);
    }

    [Fact]
    public void Extractor_RequiredMissing_ProducesError()
    {
        var registry = new OptionRegistry();
        registry.Add(new OptionMetadata("path", Array.Empty<string>(), TakesValue: true, Required: true));

        var tokens = Lexer.Tokenize(Array.Empty<string>());
        var (_, _, errors) = GlobalOptionExtractor.Extract(tokens, registry);

        Assert.Single(errors);
        Assert.Contains("path", errors[0]);
    }

    [Fact]
    public void Extractor_UnknownOption_ProducesError()
    {
        var registry = new OptionRegistry();
        registry.Add(new OptionMetadata("known", Array.Empty<string>(), TakesValue: false, Required: false));

        var tokens = Lexer.Tokenize(["--mystery"]);
        var (_, _, errors) = GlobalOptionExtractor.Extract(tokens, registry);

        Assert.Single(errors);
        Assert.Contains("mystery", errors[0]);
    }

    [Fact]
    public void Extractor_MissingValueForValueBearing_ProducesError()
    {
        var registry = new OptionRegistry();
        registry.Add(new OptionMetadata("format", Array.Empty<string>(), TakesValue: true, Required: false));

        var tokens = Lexer.Tokenize(["--format"]);
        var (_, _, errors) = GlobalOptionExtractor.Extract(tokens, registry);

        Assert.Single(errors);
        Assert.Contains("format", errors[0]);
    }
}
