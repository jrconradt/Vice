using Vice.Mux.Strategies;
using Xunit;

namespace Vice.Mux.Tests;

public class StrategyPropertyTests
{
    private static StrategyRegistry NewRegistry() => StrategyRegistry.Default();

    [Fact]
    public void Random_AdvancesDeterministicallyFromZeroSeed()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("random", out var entry));

        var a = new RouteState { Seed = 0 };
        var b = new RouteState { Seed = 0 };
        byte[] chunk = { 7 };

        for (var i = 0; i < 256; i++)
        {
            var ia = entry.Route!(chunk, 16, a);
            var ib = entry.Route!(chunk, 16, b);
            Assert.Equal(ia, ib);
            Assert.InRange(ia, 0, 15);
        }

        Assert.NotEqual(0UL, a.Seed);
    }

    [Fact]
    public void Random_SeedAdvancesEachCall()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("random", out var entry));

        var state = new RouteState { Seed = 0 };
        byte[] chunk = { 1 };
        var seeds = new HashSet<ulong>();
        for (var i = 0; i < 64; i++)
        {
            entry.Route!(chunk, 8, state);
            Assert.True(seeds.Add(state.Seed));
        }
    }

    [Fact]
    public void Random_ProducesInRangeAcrossManyChunks()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("random", out var entry));

        var state = new RouteState { Seed = 12345 };
        var hits = new int[10];
        byte[] chunk = { 0 };
        for (var i = 0; i < 100000; i++)
        {
            hits[entry.Route!(chunk, hits.Length, state)]++;
        }

        foreach (var h in hits)
        {
            Assert.True(h > 0);
        }
    }

    [Fact]
    public void Weighted_Configure_NegativeWeightThrows()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("weighted", out var entry));
        Assert.NotNull(entry.Configure);
        var state = new RouteState();
        Assert.Throws<ArgumentException>(() => entry.Configure!(state, "-1,2"));
    }

    [Fact]
    public void Weighted_Configure_NonIntegerWeightThrows()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("weighted", out var entry));
        var state = new RouteState();
        Assert.Throws<ArgumentException>(() => entry.Configure!(state, "a,b"));
    }

    [Fact]
    public void Weighted_EmptyWeights_FallsBackToRoundRobin()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("weighted", out var entry));
        var state = new RouteState();
        byte[] chunk = { 0 };
        var hits = new int[3];
        for (var i = 0; i < 300; i++)
        {
            hits[entry.Route!(chunk, 3, state)]++;
        }

        Assert.All(hits, h => Assert.Equal(100, h));
    }

    [Fact]
    public void Weighted_ZeroTotalWeights_FallsBackToRoundRobin()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("weighted", out var entry));
        var state = new RouteState();
        entry.Configure!(state, "0,0,0");
        byte[] chunk = { 0 };
        var hits = new int[3];
        for (var i = 0; i < 300; i++)
        {
            hits[entry.Route!(chunk, 3, state)]++;
        }

        Assert.All(hits, h => Assert.Equal(100, h));
    }

    [Theory]
    [InlineData("5,1")]
    [InlineData("1,2,1")]
    [InlineData("7,3")]
    [InlineData("2,2,2,2")]
    public void Weighted_DistributionMatchesWeights(string spec)
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("weighted", out var entry));
        var state = new RouteState();
        entry.Configure!(state, spec);

        var weights = spec.Split(',').Select(int.Parse).ToArray();
        var n = weights.Length;
        var total = weights.Sum();
        var cycles = 1000;
        var hits = new int[n];
        byte[] chunk = { 0 };
        for (var i = 0; i < cycles * total; i++)
        {
            hits[entry.Route!(chunk, n, state)]++;
        }

        for (var i = 0; i < n; i++)
        {
            Assert.Equal(weights[i] * cycles, hits[i]);
        }
    }

    [Fact]
    public void StickyKey_EmptyChunk_ReturnsZero()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("sticky-key", out var entry));
        var state = new RouteState { KeyOffset = 5, KeyLength = 10 };
        var idx = entry.Route!(ReadOnlySpan<byte>.Empty, 8, state);
        Assert.Equal(0, idx);
    }

    [Fact]
    public void StickyKey_OffsetBeyondChunk_ClampsWithoutFaulting()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("sticky-key", out var entry));
        var state = new RouteState { KeyOffset = 1000, KeyLength = 1000 };
        byte[] chunk = { 9, 8, 7 };
        var idx = entry.Route!(chunk, 4, state);
        Assert.InRange(idx, 0, 3);
    }

}
