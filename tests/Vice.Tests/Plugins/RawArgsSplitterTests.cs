using System.Reflection;
using Vice.Host;
using Xunit;

namespace Vice.Tests.Plugins;

public class RawArgsSplitterTests
{
    private static readonly Type SplitterType = typeof(Vice.Host.ViceApp).Assembly
        .GetType("Vice.Plugins.RawArgsSplitter")!;

    private static bool ContainsPiping(string[] args)
        => (bool)SplitterType.GetMethod("ContainsPiping", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { args })!;

    private static (string[] Args, string? OperatorWord)[] Split(string[] args)
    {
        var list = (System.Collections.IEnumerable)SplitterType.GetMethod("Split", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { args })!;
        var result = new List<(string[], string?)>();
        foreach (var item in list)
        {
            var t = item.GetType();
            var a = (string[])t.GetProperty("Args")!.GetValue(item)!;
            var op = (string?)t.GetProperty("OperatorWord")!.GetValue(item);
            result.Add((a, op));
        }
        return result.ToArray();
    }

    [Fact]
    public void NoPipingWords_SingleSegmentNoOperator()
    {
        Assert.False(ContainsPiping(new[] { "read", "/file" }));
        var segs = Split(new[] { "read", "/file" });
        Assert.Single(segs);
        Assert.Null(segs[0].OperatorWord);
        Assert.Equal(new[] { "read", "/file" }, segs[0].Args);
    }

    [Fact]
    public void ThenSplits_TwoSegments()
    {
        Assert.True(ContainsPiping(new[] { "read", "/f", "then", "write", "to", "file", "/g" }));
        var segs = Split(new[] { "read", "/f", "then", "write", "to", "file", "/g" });
        Assert.Equal(2, segs.Length);
        Assert.Null(segs[0].OperatorWord);
        Assert.Equal(new[] { "read", "/f" }, segs[0].Args);
        Assert.Equal("then", segs[1].OperatorWord);
        Assert.Equal(new[] { "write", "to", "file", "/g" }, segs[1].Args);
    }

    [Fact]
    public void MultiplePipingWords_NSegments()
    {
        var segs = Split(new[] { "a", "then", "b", "pipe", "c", "and", "d" });
        Assert.Equal(4, segs.Length);
        Assert.Equal(new[] { "a" }, segs[0].Args);
        Assert.Equal(new[] { "b" }, segs[1].Args);
        Assert.Equal(new[] { "c" }, segs[2].Args);
        Assert.Equal(new[] { "d" }, segs[3].Args);
        Assert.Equal(new string?[] { null, "then", "pipe", "and" }, new[] { segs[0].OperatorWord, segs[1].OperatorWord, segs[2].OperatorWord, segs[3].OperatorWord });
    }

    [Fact]
    public void PipingWordsAreCaseInsensitive()
    {
        Assert.True(ContainsPiping(new[] { "a", "THEN", "b" }));
        Assert.True(ContainsPiping(new[] { "a", "Pipe", "b" }));
    }

    [Fact]
    public void EmptyArgs_SingleEmptySegment()
    {
        var segs = Split(Array.Empty<string>());
        Assert.Single(segs);
        Assert.Empty(segs[0].Args);
    }
}

public class RawArgsSplitterFanTests
{
    private static readonly Type SplitterType = typeof(Vice.Host.ViceApp).Assembly
        .GetType("Vice.Plugins.RawArgsSplitter")!;

    private static bool ContainsFan(string[] args)
        => (bool)SplitterType.GetMethod("ContainsFan", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { args })!;

    private static (string[] Args, string? OperatorWord)[] Split(string[] args)
    {
        var list = (System.Collections.IEnumerable)SplitterType.GetMethod("Split", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { args })!;
        var result = new List<(string[], string?)>();
        foreach (var item in list)
        {
            var t = item.GetType();
            var a = (string[])t.GetProperty("Args")!.GetValue(item)!;
            var op = (string?)t.GetProperty("OperatorWord")!.GetValue(item);
            result.Add((a, op));
        }
        return result.ToArray();
    }

    [Fact]
    public void FanSplitsLikeOtherPipingWords()
    {
        Assert.True(ContainsFan(new[] { "a", "fan", "b" }));
        var segs = Split(new[] { "read", "F", "then", "a", "fan", "b", "fan", "c" });
        Assert.Equal(4, segs.Length);
        Assert.Equal("then", segs[1].OperatorWord);
        Assert.Equal("fan", segs[2].OperatorWord);
        Assert.Equal("fan", segs[3].OperatorWord);
    }

    [Fact]
    public void FanIsCaseInsensitive()
    {
        Assert.True(ContainsFan(new[] { "a", "FAN", "b" }));
        Assert.True(ContainsFan(new[] { "a", "Fan", "b" }));
    }
}
