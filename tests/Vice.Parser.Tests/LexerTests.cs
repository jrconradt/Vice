using Vice.Parser;
using Xunit;

namespace Vice.Parser.Tests;

public class LexerTests
{
    [Fact]
    public void Word_ProducesWordToken()
    {
        var tokens = Lexer.Tokenize(["add"]);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Word, tokens[0].Kind);
        Assert.Equal("add", tokens[0].Value);
    }

    [Fact]
    public void MultipleWords_ProduceWordTokens()
    {
        var tokens = Lexer.Tokenize(["add", "user", "jeff"]);
        Assert.Equal(3, tokens.Count);
        Assert.All(tokens, t => Assert.Equal(TokenKind.Word, t.Kind));
    }

    [Fact]
    public void GlobalOption_ProducesGlobalOptionToken()
    {
        var tokens = Lexer.Tokenize(["--verbose"]);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.GlobalOption, tokens[0].Kind);
        Assert.Equal("verbose", tokens[0].Value);
    }

    [Fact]
    public void QuotedArg_ProducesQuotedToken()
    {
        var tokens = Lexer.Tokenize(["\"hello world\""]);
        Assert.Single(tokens);
        Assert.Equal(TokenKind.Quoted, tokens[0].Kind);
        Assert.Equal("hello world", tokens[0].Value);
    }

    [Fact]
    public void CommaSeparated_ProducesCommaSeparatorTokens()
    {
        var tokens = Lexer.Tokenize(["a,b,c"]);
        Assert.Equal(5, tokens.Count);
        Assert.Equal(TokenKind.Word, tokens[0].Kind);
        Assert.Equal(TokenKind.CommaSeparator, tokens[1].Kind);
        Assert.Equal(TokenKind.Word, tokens[2].Kind);
        Assert.Equal(TokenKind.CommaSeparator, tokens[3].Kind);
        Assert.Equal(TokenKind.Word, tokens[4].Kind);
    }

    [Fact]
    public void EmptyArg_IsSkipped()
    {
        var tokens = Lexer.Tokenize(["add", "", "user"]);
        Assert.Equal(2, tokens.Count);
    }

    [Fact]
    public void StringOverload_SplitsOnWhitespace()
    {
        var tokens = Lexer.Tokenize("add user jeff");
        Assert.Equal(3, tokens.Count);
        Assert.Equal("add", tokens[0].Value);
        Assert.Equal("user", tokens[1].Value);
        Assert.Equal("jeff", tokens[2].Value);
    }

    [Fact]
    public void StringOverload_PreservesQuotedStrings()
    {
        var tokens = Lexer.Tokenize("add user \"john doe\"");
        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenKind.Quoted, tokens[2].Kind);
        Assert.Equal("john doe", tokens[2].Value);
    }
}
