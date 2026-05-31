using CsCheck;
using Vice.Mux.Strategies;
using Xunit;

namespace Vice.Mux.Tests;

public class StrategyRoutingPropertyTests
{
    private const long Iterations = 20_000;

    private static readonly string[] UnicastStrategies =
    {
        "roundrobin",
        "hash",
        "random",
        "sticky-key",
        "weighted"
    };

    private static readonly Gen<byte[]> Chunk =
        Gen.Byte.Array[0, 32];

    private static readonly Gen<int[]> Weights =
        Gen.Int[0, 10].Array[0, 5];

    private static readonly Gen<RouteState> State =
        Gen.Select(
            Gen.Long,
            Gen.Long,
            Gen.UInt,
            Gen.ULong,
            Weights,
            Gen.Int[-8, 64],
            Gen.Int[0, 64],
            (chunkIndex, byteCount, cursor, seed, weights, keyOffset, keyLength) => new RouteState
            {
                ChunkIndex = chunkIndex,
                ByteCount = byteCount,
                Cursor = cursor,
                Seed = seed,
                Weights = weights,
                KeyOffset = keyOffset,
                KeyLength = keyLength
            });

    [Theory]
    [InlineData("roundrobin")]
    [InlineData("hash")]
    [InlineData("random")]
    [InlineData("sticky-key")]
    [InlineData("weighted")]
    public void Unicast_AlwaysReturnsIndexInRange(string name)
    {
        var registry = StrategyRegistry.Default();
        Assert.True(registry.TryGet(name, out var entry));
        Assert.NotNull(entry.Route);

        Gen.Select(Gen.Int[1, 256], Chunk, State).Sample(triple =>
            {
                var (sinkCount, chunk, state) = triple;
                var idx = entry.Route!(chunk, sinkCount, state);
                Assert.InRange(idx, 0, sinkCount - 1);
            },
            iter: Iterations,
            seed: $"0000RouteUnicast_{name}");
    }

    [Fact]
    public void StickyKey_ArbitraryOffsetsAndLengths_StayInRange()
    {
        var registry = StrategyRegistry.Default();
        Assert.True(registry.TryGet("sticky-key", out var entry));

        var stickyState = Gen.Select(
            Gen.Int[-8, 64],
            Gen.Int[0, 64],
            Gen.ULong,
            (keyOffset, keyLength, seed) => new RouteState
            {
                KeyOffset = keyOffset,
                KeyLength = keyLength,
                Seed = seed
            });

        Gen.Select(Gen.Int[1, 64], Chunk, stickyState).Sample(triple =>
            {
                var (sinkCount, chunk, state) = triple;
                var idx = entry.Route!(chunk, sinkCount, state);
                Assert.InRange(idx, 0, sinkCount - 1);
            },
            iter: Iterations,
            seed: "0000StickyInRange0");
    }

    [Fact]
    public void AllUnicast_EmptyChunk_ReturnsValidIndex()
    {
        var registry = StrategyRegistry.Default();
        State.Sample(state =>
            {
                foreach (var name in UnicastStrategies)
                {
                    Assert.True(registry.TryGet(name, out var entry));
                    var idx = entry.Route!(ReadOnlySpan<byte>.Empty, 4, state);
                    Assert.InRange(idx, 0, 3);
                }
            },
            iter: Iterations,
            seed: "0000EmptyChunkRoute");
    }
}
