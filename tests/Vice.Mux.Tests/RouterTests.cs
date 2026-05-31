using System.Text;
using Vice.Mux.Routing;
using Xunit;

namespace Vice.Mux.Tests;

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

        var code = await Router.RouteAsync(1, clauses, input, 4, CancellationToken.None);

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

        var code = await Router.RouteAsync(9, clauses, input, 4, CancellationToken.None);

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

        var code = await Router.RouteAsync(5, clauses, input, 8, CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Equal(payload, await File.ReadAllBytesAsync(paths.Files[0]));
        Assert.Equal(payload, await File.ReadAllBytesAsync(paths.Files[1]));
    }
}
