using CsCheck;
using Vice.Display.Rendering;
using Xunit;

namespace Vice.Tests;

public class AnsiStripperPropertyTests
{
    private const long ITERATIONS = 20_000;

    private static readonly Gen<string> Printable =
        Gen.String[Gen.Char[" abAB09._-/?[]"], 0, 6];

    private static readonly Gen<string> CsiSequence =
        Gen.Select(Gen.String[Gen.Char["0123456789;?"], 0, 5],
                   Gen.Char["ABCHJKmhlr"],
                   (body, final) => $"\u001b[{body}{final}");

    private static readonly Gen<string> OscBel =
        Gen.String[Gen.Char["abAB09 ;/:"], 0, 6].Select(body => $"\u001b]{body}\u0007");

    private static readonly Gen<string> OscSt =
        Gen.String[Gen.Char["abAB09 ;/:"], 0, 6].Select(body => $"\u001b]{body}\u001b\\");

    private static readonly Gen<string> Charset =
        Gen.Char["()B0AU"].Select(c => $"\u001b({c}");

    private static readonly Gen<string> LoneControl =
        Gen.OneOfConst("\u001b", "\r", "\b", "\u007f", "\u0000", "\u0007", "\t", "\n");

    private static readonly Gen<string> Fragment =
        Gen.OneOf(Printable, CsiSequence, OscBel, OscSt, Charset, LoneControl);

    private static readonly Gen<string> TerminalText =
        Fragment.Array[0, 12].Select(parts => string.Concat(parts));

    [Fact]
    public void Strip_TerminalText_NeverThrows()
    {
        TerminalText.Sample(input =>
            {
                var ex = Record.Exception(() => AnsiStripper.Strip(input));
                Assert.Null(ex);
            },
            iter: ITERATIONS,
            seed: "0000AnsiNoThrow000");
    }

    [Fact]
    public void Strip_TerminalText_IsIdempotent()
    {
        TerminalText.Sample(input =>
            {
                var once = AnsiStripper.Strip(input);
                var twice = AnsiStripper.Strip(once);
                Assert.Equal(once, twice);
            },
            iter: ITERATIONS,
            seed: "0000AnsiIdempot000");
    }

    [Fact]
    public void Strip_TerminalText_LeavesNoEscapeOrUnsafeControl()
    {
        TerminalText.Sample(input =>
            {
                var stripped = AnsiStripper.Strip(input);
                foreach (var c in stripped)
                {
                    bool isAllowedWhitespace = c == '\n'
                        || c == '\t';
                    bool isControl = c < ' '
                        || c == '\u007f';
                    if (isControl)
                    {
                        Assert.True(isAllowedWhitespace, $"control byte U+{(int)c:X4} survived stripping");
                    }
                }
            },
            iter: ITERATIONS,
            seed: "0000AnsiNoControl0");
    }

    [Fact]
    public void Strip_TerminalText_RemovesOnly()
    {
        TerminalText.Sample(input =>
            {
                var stripped = AnsiStripper.Strip(input);
                int i = 0;
                foreach (var c in stripped)
                {
                    while (i < input.Length
                        && input[i] != c)
                    {
                        i++;
                    }
                    Assert.True(i < input.Length, "stripped output introduced a character not present in the input");
                    i++;
                }
            },
            iter: ITERATIONS,
            seed: "0000AnsiRemoveOnly");
    }
}
