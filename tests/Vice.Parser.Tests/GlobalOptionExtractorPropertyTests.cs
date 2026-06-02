using CsCheck;
using Vice.Parser;
using Xunit;

namespace Vice.Parser.Tests;

public class GlobalOptionExtractorPropertyTests
{
    private const long Iterations = 5_000;

    private static readonly Gen<string> PlainWord =
        Gen.String[Gen.Char["abcXYZ0123"], 1, 8];

    private static readonly Gen<string> KnownFlag =
        Gen.OneOfConst("--help", "--version", "--verbose");

    [Fact]
    public void NoGlobalOptions_AllTokensSurviveInOrder()
    {
        PlainWord.Array[0, 10].Sample(words =>
            {
                var tokens = Lexer.Tokenize(words);
                var (remaining, globals) = GlobalOptionExtractor.Extract(tokens);

                Assert.Empty(globals);
                Assert.Equal(tokens.Count, remaining.Count);
                for (int i = 0; i < tokens.Count; i++)
                {
                    Assert.Equal(tokens[i].Kind, remaining[i].Kind);
                    Assert.Equal(tokens[i].Value, remaining[i].Value);
                }
            },
            iter: Iterations,
            seed: "0000GlobalSurvive0");
    }

    [Fact]
    public void KnownFlags_AreExtractedAndRemovedFromRemaining()
    {
        var mixed = Gen.OneOf(
            PlainWord.Select(w => (Value: w, IsFlag: false)),
            KnownFlag.Select(f => (Value: f, IsFlag: true)));

        mixed.Array[0, 12].Sample(items =>
            {
                var args = items.Select(i => i.Value).ToArray();
                var tokens = Lexer.Tokenize(args);
                var (remaining, globals) = GlobalOptionExtractor.Extract(tokens);

                int distinctFlagCount = items
                    .Where(i => i.IsFlag)
                    .Select(i => i.Value.TrimStart('-'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                int wordCount = items.Count(i => !i.IsFlag);

                Assert.Equal(distinctFlagCount, globals.Count);
                Assert.Equal(wordCount, remaining.Count);
                Assert.All(remaining, t => Assert.NotEqual(TokenKind.GlobalOption, t.Kind));
            },
            iter: Iterations,
            seed: "0000GlobalFlags00");
    }

    [Fact]
    public void Extract_PreservesNonOptionWordValues()
    {
        PlainWord.Array[1, 8].Sample(words =>
            {
                var tokens = Lexer.Tokenize(words);
                var (remaining, _) = GlobalOptionExtractor.Extract(tokens);

                var survivedWords = remaining
                    .Where(t => t.Kind == TokenKind.Word)
                    .Select(t => t.Value)
                    .ToArray();

                Assert.Equal(words, survivedWords);
            },
            iter: Iterations,
            seed: "0000GlobalPreserve");
    }
}
