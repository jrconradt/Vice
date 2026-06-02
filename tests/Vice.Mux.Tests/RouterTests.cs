using System.Text;
using CsCheck;
using Vice.Logging;
using Vice.Mux.Routing;
using Xunit;

namespace Vice.Mux.Tests;

public class ConditionPropertyTests
{
    private const long ITERATIONS = 10_000;

    private static readonly Gen<int[]> CodeList =
        Gen.Int[-1000, 1000].Array[1, 8];

    private static readonly Gen<int> AnyCode =
        Gen.Int[-2000, 2000];

    [Fact]
    public void CommaList_MatchesEveryListedCode()
    {
        CodeList.Sample(codes =>
            {
                var spec = string.Join(',', codes);

                var condition = Condition.Parse(spec);

                Assert.False(condition.IsWildcard);
                for (int i = 0; i < codes.Length; i++)
                {
                    Assert.True(condition.Matches(codes[i]));
                }
            },
            iter: ITERATIONS,
            seed: "0000CondListMatch0");
    }

    [Fact]
    public void CommaList_MatchesIffMemberOfSet()
    {
        Gen.Select(CodeList,
                   AnyCode,
                   (codes, probe) => (codes, probe)).Sample(pair =>
            {
                var (codes, probe) = pair;
                var spec = string.Join(',', codes);

                var condition = Condition.Parse(spec);

                Assert.Equal(Array.IndexOf(codes, probe) >= 0, condition.Matches(probe));
            },
            iter: ITERATIONS,
            seed: "0000CondMemberSet0");
    }

    [Fact]
    public void Wildcard_MatchesEveryCode()
    {
        AnyCode.Sample(code =>
            {
                Assert.True(Condition.Parse("*").Matches(code));
                Assert.True(Condition.Parse("all").Matches(code));
            },
            iter: ITERATIONS,
            seed: "0000CondWildcard00");
    }

    [Fact]
    public void Parse_IntegerCommaList_NeverThrows()
    {
        CodeList.Sample(codes =>
            {
                var spec = string.Join(',', codes);

                var ex = Record.Exception(() => Condition.Parse(spec));

                Assert.Null(ex);
            },
            iter: ITERATIONS,
            seed: "0000CondNoThrow000");
    }

    private static readonly Gen<int[]> FullRangeCodeList =
        Gen.Int.Array[1, 8];

    [Fact]
    public void FullRangeCommaList_RoundTrips()
    {
        FullRangeCodeList.Sample(codes =>
            {
                var spec = string.Join(',', codes);

                var condition = Condition.Parse(spec);

                Assert.False(condition.IsWildcard);
                for (int i = 0; i < codes.Length; i++)
                {
                    Assert.True(condition.Matches(codes[i]));
                }
            },
            iter: ITERATIONS,
            seed: "0000CondFullRange0");
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void BoundaryCode_RoundTrips(int code)
    {
        var condition = Condition.Parse($"{code}");

        Assert.False(condition.IsWildcard);
        Assert.True(condition.Matches(code));
    }

    private static readonly Gen<string> SpecFragment =
        Gen.OneOf(Gen.Int.Select(static n => $"{n}"),
                  Gen.OneOfConst($"{int.MinValue}",
                                 $"{int.MaxValue}",
                                 "9999999999999999999",
                                 "-9999999999999999999",
                                 "",
                                 "   ",
                                 "+5",
                                 "nope",
                                 "*",
                                 "all",
                                 "0x1F",
                                 " 7 "),
                  Gen.String[Gen.Char[" ,*-+0123abAB"], 0, 6]);

    private static readonly Gen<string> ArbitrarySpec =
        SpecFragment.Array[0, 6].Select(static parts => string.Join(',', parts));

    [Fact]
    public void Parse_ArbitrarySpec_ThrowsOnlyArgumentException()
    {
        ArbitrarySpec.Sample(spec =>
            {
                var ex = Record.Exception(() => Condition.Parse(spec));

                Assert.True(ex is null || ex is ArgumentException);
            },
            iter: ITERATIONS,
            seed: "0000CondArbSpec00");
    }
}

public class ConditionTests
{
    [Fact]
    public void Single_MatchesOnlyThatCode()
    {
        var c = Condition.Parse("0");
        Assert.True(c.Matches(0));
        Assert.False(c.Matches(1));
    }

    [Fact]
    public void List_MatchesAnyListedCode()
    {
        var c = Condition.Parse("1,2");
        Assert.True(c.Matches(1));
        Assert.True(c.Matches(2));
        Assert.False(c.Matches(0));
        Assert.False(c.Matches(3));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("all")]
    [InlineData("ALL")]
    public void Wildcard_MatchesEverything(string spec)
    {
        var c = Condition.Parse(spec);
        Assert.True(c.IsWildcard);
        Assert.True(c.Matches(0));
        Assert.True(c.Matches(137));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanumber")]
    [InlineData("1,nope")]
    public void Invalid_Throws(string spec)
    {
        Assert.Throws<ArgumentException>(() => Condition.Parse(spec));
    }
}

public class RouterTests
{
    private sealed class SinkPaths : IDisposable
    {
        public string Dir { get; }
        public string[] Files { get; }

        public SinkPaths(int count)
        {
            Dir = Path.Combine(Path.GetTempPath(), $"vice-mux-route-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Dir);
            Files = new string[count];
            for (int i = 0; i < count; i++)
            {
                Files[i] = Path.Combine(Dir, $"sink-{i}.bin");
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }

    [Fact]
    public async Task Code_SelectsTheMatchingClauseSink()
    {
        using var paths = new SinkPaths(3);
        var clauses = new[]
        {
            new RouteClause(Condition.Parse("0"), $"file:{paths.Files[0]}"),
            new RouteClause(Condition.Parse("1,2"), $"file:{paths.Files[1]}"),
            new RouteClause(Condition.Parse("*"), $"file:{paths.Files[2]}"),
        };
        var input = new MemoryStream(Encoding.ASCII.GetBytes("payload"));

        var code = await Router.RouteAsync(1, clauses, input, 4, CancellationToken.None, NullViceLogger.Instance);

        Assert.Equal(0, code);
        Assert.False(File.Exists(paths.Files[0]));
        Assert.Equal("payload", await File.ReadAllTextAsync(paths.Files[1]));
        Assert.Equal("payload", await File.ReadAllTextAsync(paths.Files[2]));
    }

    [Fact]
    public async Task ZeroMatches_WritesNothing()
    {
        using var paths = new SinkPaths(1);
        var clauses = new[]
        {
            new RouteClause(Condition.Parse("0"), $"file:{paths.Files[0]}"),
        };
        var input = new MemoryStream(Encoding.ASCII.GetBytes("payload"));

        var code = await Router.RouteAsync(9, clauses, input, 4, CancellationToken.None, NullViceLogger.Instance);

        Assert.Equal(0, code);
        Assert.False(File.Exists(paths.Files[0]));
    }

    [Fact]
    public async Task MultipleMatches_BroadcastIdenticalBytes()
    {
        using var paths = new SinkPaths(2);
        var payload = Encoding.ASCII.GetBytes("the-same-everywhere");
        var clauses = new[]
        {
            new RouteClause(Condition.Parse("*"), $"file:{paths.Files[0]}"),
            new RouteClause(Condition.Parse("5"), $"file:{paths.Files[1]}"),
        };
        var input = new MemoryStream(payload);

        var code = await Router.RouteAsync(5, clauses, input, 8, CancellationToken.None, NullViceLogger.Instance);

        Assert.Equal(0, code);
        Assert.Equal(payload, await File.ReadAllBytesAsync(paths.Files[0]));
        Assert.Equal(payload, await File.ReadAllBytesAsync(paths.Files[1]));
    }
}
