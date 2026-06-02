using Vice.Parser;
using Xunit;

namespace Vice.Parser.Tests;

public class LexerEdgeCaseTests
{
    [Fact]
    public void UnterminatedQuote_Array_ProducesWordRetainingQuote()
    {
        var tokens = Lexer.Tokenize(["\"oops"]);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Word, tokens[0].Kind);
        Assert.Equal("\"oops", tokens[0].Value);
    }

    [Fact]
    public void UnterminatedQuote_String_KeepsRemainderAsWord()
    {
        var tokens = Lexer.Tokenize("add \"oops");
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Word, tokens[0].Kind);
        Assert.Equal("add", tokens[0].Value);
        Assert.Equal(TokenKind.Word, tokens[1].Kind);
        Assert.Equal("\"oops", tokens[1].Value);
    }

    [Fact]
    public void QuotedTokenWithEmbeddedSpaces_String_RoundTripsValue()
    {
        var tokens = Lexer.Tokenize("a \"b c\" d");
        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenKind.Word, tokens[0].Kind);
        Assert.Equal("a", tokens[0].Value);
        Assert.Equal(TokenKind.Quoted, tokens[1].Kind);
        Assert.Equal("b c", tokens[1].Value);
        Assert.Equal(TokenKind.Word, tokens[2].Kind);
        Assert.Equal("d", tokens[2].Value);
    }

    [Fact]
    public void SingleQuoteChar_String_ProducesQuotedToken()
    {
        var tokens = Lexer.Tokenize("'hello world'");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Quoted, tokens[0].Kind);
        Assert.Equal("hello world", tokens[0].Value);
    }

    [Fact]
    public void BackslashInWord_IsLiteralNotEscape()
    {
        var tokens = Lexer.Tokenize(["a\\b"]);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Word, tokens[0].Kind);
        Assert.Equal("a\\b", tokens[0].Value);
    }

    [Fact]
    public void EscapedQuoteInsideUnquotedArg_KeepsLiteralQuotes()
    {
        var tokens = Lexer.Tokenize(["foo\"bar\""]);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Word, tokens[0].Kind);
        Assert.Equal("foo\"bar\"", tokens[0].Value);
    }

    [Fact]
    public void EmptyCommaFields_AreSkippedButSeparatorsRemain()
    {
        var tokens = Lexer.Tokenize(["a,,b"]);
        Assert.Equal(4, tokens.Count);
        Assert.Equal(TokenKind.Word, tokens[0].Kind);
        Assert.Equal("a", tokens[0].Value);
        Assert.Equal(TokenKind.CommaSeparator, tokens[1].Kind);
        Assert.Equal(TokenKind.CommaSeparator, tokens[2].Kind);
        Assert.Equal(TokenKind.Word, tokens[3].Kind);
        Assert.Equal("b", tokens[3].Value);
    }

    [Fact]
    public void OnlyCommas_ProduceOnlySeparators()
    {
        var tokens = Lexer.Tokenize([",,"]);
        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, t => Assert.Equal(TokenKind.CommaSeparator, t.Kind));
    }

    [Fact]
    public void RunsOfWhitespace_String_CollapseToSeparateWords()
    {
        var tokens = Lexer.Tokenize("a    b\t c");
        Assert.Equal(3, tokens.Count);
        Assert.Equal("a", tokens[0].Value);
        Assert.Equal("b", tokens[1].Value);
        Assert.Equal("c", tokens[2].Value);
    }
}
