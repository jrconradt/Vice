using System.Collections;
using Vice.Mux.Strategies;
using Xunit;

namespace Vice.Mux.Tests;

public class StrategyTests
{
    private static StrategyRegistry NewRegistry() => StrategyRegistry.Default();

    [Fact]
    public void RoundRobin_CyclesEvenly()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("roundrobin", out var entry));
        var state = new RouteState();
        var hits = new int[4];
        byte[] empty = new byte[1];
        for (int i = 0; i < 400; i++)
        {
            hits[entry.Route!(empty, 4, state)]++;
        }

        Assert.All(hits, h => Assert.Equal(100, h));
    }

    [Fact]
    public void Hash_SameChunkAlwaysSameSink()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("hash", out var entry));
        var state = new RouteState();
        ReadOnlySpan<byte> payload = "hello world"u8;
        var first = entry.Route!(payload, 7, state);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(first, entry.Route!(payload, 7, state));
        }
    }

    [Fact]
    public void Hash_DistributesAcrossManyKeys()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("hash", out var entry));
        var state = new RouteState();
        var hits = new int[8];
        byte[] buf = new byte[4];
        for (int i = 0; i < 8000; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf, i);
            hits[entry.Route!(buf, 8, state)]++;
        }
        foreach (var h in hits)
        {
            Assert.InRange(h, 800, 1200);
        }
    }

    [Fact]
    public void Broadcast_AllBitsSet()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("broadcast", out var entry));
        var mask = new BitArray(5);
        entry.Broadcast!(ReadOnlySpan<byte>.Empty, 5, new RouteState(), mask);
        for (int i = 0; i < 5; i++)
        {
            Assert.True(mask[i]);
        }
    }

    [Fact]
    public void Broadcast_HandlesMoreThan64Sinks()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("broadcast", out var entry));
        const int n = 100;
        var mask = new BitArray(n);
        entry.Broadcast!(ReadOnlySpan<byte>.Empty, n, new RouteState(), mask);
        for (int i = 0; i < n; i++)
        {
            Assert.True(mask[i]);
        }
    }

    [Fact]
    public void StickyKey_PartitionsByKeySlice()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("sticky-key", out var entry));
        var state = new RouteState { KeyOffset = 0, KeyLength = 1 };
        var a1 = entry.Route!("A_payload_one"u8, 4, state);
        var a2 = entry.Route!("A_payload_two"u8, 4, state);
        var b1 = entry.Route!("B_payload_one"u8, 4, state);
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b1);
    }

    [Fact]
    public void Weighted_RespectsRatio()
    {
        var r = NewRegistry();
        Assert.True(r.TryGet("weighted", out var entry));
        var state = new RouteState();
        entry.Configure!(state, "3,1");
        var hits = new int[2];
        byte[] empty = new byte[1];
        for (int i = 0; i < 400; i++)
        {
            hits[entry.Route!(empty, 2, state)]++;
        }

        Assert.Equal(300, hits[0]);
        Assert.Equal(100, hits[1]);
    }

    [Fact]
    public void Registry_ListsAllBuiltins()
    {
        var r = NewRegistry();
        var names = r.All.Select(e => e.Name).ToHashSet();
        Assert.Contains("roundrobin", names);
        Assert.Contains("hash", names);
        Assert.Contains("random", names);
        Assert.Contains("broadcast", names);
        Assert.Contains("sticky-key", names);
        Assert.Contains("weighted", names);
    }
}
