using Vice.Parser;
using Xunit;

namespace Vice.Parser.Tests;

public class GlobalOptionExtractorTests
{
    private static IReadOnlyList<Token> Tokenize(params string[] args) => Lexer.Tokenize(args);

    [Fact]
    public void KnownFlag_ExtractedToGlobals()
    {
        var tokens = Tokenize("add", "--verbose", "user");
        var (remaining, globals) = GlobalOptionExtractor.Extract(tokens);
        Assert.True(globals.ContainsKey("verbose"));
        Assert.Equal(2, remaining.Count);
    }

    [Fact]
    public void ValueBearingOption_ConsumesNextToken()
    {
        var tokens = Tokenize("add", "--format", "json", "user");
        var (remaining, globals) = GlobalOptionExtractor.Extract(tokens);
        Assert.Equal("json", globals["format"]);
        Assert.Equal(2, remaining.Count);
    }

    [Fact]
    public void UnknownFlag_WithNextToken_TreatedAsValueBearer()
    {

        var tokens = Tokenize("add", "--output", "file.txt");
        var (remaining, globals) = GlobalOptionExtractor.Extract(tokens);
        Assert.True(globals.ContainsKey("output"));
        Assert.Equal("file.txt", globals["output"]);
        Assert.Single(remaining);
    }

    [Fact]
    public void NoOptions_AllTokensRemain()
    {
        var tokens = Tokenize("add", "user", "jeff");
        var (remaining, globals) = GlobalOptionExtractor.Extract(tokens);
        Assert.Equal(3, remaining.Count);
        Assert.Empty(globals);
    }

    [Fact]
    public void KnownFlagAtEnd_HasNullValue()
    {
        var tokens = Tokenize("add", "--help");
        var (remaining, globals) = GlobalOptionExtractor.Extract(tokens);
        Assert.True(globals.ContainsKey("help"));
        Assert.Null(globals["help"]);
        Assert.Single(remaining);
    }
}
