using CsCheck;
using Vice.Parser;
using Xunit;

namespace Vice.Parser.Tests;

public class LexerPropertyTests
{
    private const long Iterations = 10_000;

    private static readonly Gen<string> StructuralInput =
        Gen.String[Gen.Char[" \"',-abAB"], 0, 24];

    private static readonly Gen<string> WordChar =
        Gen.Char["abAB09._"].Select(c => $"{c}");

    private static readonly Gen<string> Word =
        WordChar.Array[1, 8].Select(parts => string.Concat(parts));

    private static readonly Gen<string[]> WhitespaceWords =
        Word.Array[0, 6];

    [Fact]
    public void Tokenize_String_NeverThrows()
    {
        StructuralInput.Sample(input =>
            {
                var ex = Record.Exception(() => Lexer.Tokenize(input));
                Assert.Null(ex);
            },
            iter: Iterations,
            seed: "0000LexerNoThrowStr");
    }

    [Fact]
    public void Tokenize_Array_NeverThrows()
    {
        StructuralInput.Array[0, 8].Sample(args =>
            {
                var ex = Record.Exception(() => Lexer.Tokenize(args));
                Assert.Null(ex);
            },
            iter: Iterations,
            seed: "0000LexerNoThrowArr");
    }

    [Fact]
    public void Tokenize_QuoteFreeWords_ReconstructWhitespaceSplit()
    {
        WhitespaceWords.Sample(words =>
            {
                var input = string.Join(' ', words);
                var expected = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                var tokens = Lexer.Tokenize(input);

                Assert.Equal(expected.Length, tokens.Count);
                for (int j = 0; j < expected.Length; j++)
                {
                    Assert.Equal(TokenKind.Word, tokens[j].Kind);
                    Assert.Equal(expected[j], tokens[j].Value);
                }
            },
            iter: Iterations,
            seed: "0000LexerWordSplit0");
    }

    [Fact]
    public void Tokenize_BalancedQuotedWord_PreservesInnerValueExactly()
    {
        var inner = Gen.String[Gen.Char["abAB09 ._-"], 1, 16];
        var quote = Gen.OneOfConst('"', '\'');
        Gen.Select(inner, quote, (value, q) => (value, q)).Sample(pair =>
            {
                var (value, q) = pair;
                var quoted = $"{q}{value}{q}";

                var tokens = Lexer.Tokenize([quoted]);

                Assert.Single(tokens);
                Assert.Equal(TokenKind.Quoted, tokens[0].Kind);
                Assert.Equal(value, tokens[0].Value);
            },
            iter: Iterations,
            seed: "0000LexerQuoted000");
    }

    [Fact]
    public void Tokenize_CommaList_AlternatesWordsAndSeparators()
    {
        var field = Gen.String[Gen.Char["abAB09"], 1, 6];
        field.Array[1, 6].Sample(fields =>
            {
                var input = string.Join(',', fields);

                var tokens = Lexer.Tokenize([input]);

                int expectedSeparators = fields.Length - 1;
                int actualSeparators = tokens.Count(t => t.Kind == TokenKind.CommaSeparator);
                int actualWords = tokens.Count(t => t.Kind == TokenKind.Word);

                Assert.Equal(expectedSeparators, actualSeparators);
                Assert.Equal(fields.Length, actualWords);
            },
            iter: Iterations,
            seed: "0000LexerCommaLst0");
    }
}
