using System.Collections;
using BenchmarkDotNet.Attributes;
using Vice.Mux.Strategies;

namespace Vice.Benchmarks;

[MemoryDiagnoser]
public class RouteStrategyBenchmarks
{
    private const int SinkCount = 8;

    private const int ChunkCount = 4_096;

    private const int ChunkSize = 4_096;

    private static readonly StrategyRegistry Registry = StrategyRegistry.Default();

    [Params("roundrobin", "hash", "random", "sticky-key", "weighted")]
    public string Strategy = "roundrobin";

    private byte[][] _chunks = Array.Empty<byte[]>();
    private StrategyEntry _entry = null!;

    [GlobalSetup]
    public void Setup()
    {
        _chunks = ChunkGenerator.Build(ChunkCount, ChunkSize);
        if (!Registry.TryGet(Strategy, out _entry))
        {
            throw new InvalidOperationException($"strategy '{Strategy}' not registered");
        }
    }

    [Benchmark]
    public long Route()
    {
        var route = _entry.Route
            ?? throw new InvalidOperationException($"strategy '{Strategy}' has no route delegate");
        var state = new RouteState
        {
            Seed = 0x9E3779B97F4A7C15UL,
            KeyOffset = 0,
            KeyLength = 4,
        };
        _entry.Configure?.Invoke(state, "3:1:2:1:1:1:1:1");

        long acc = 0;
        for (var i = 0; i < _chunks.Length; i++)
        {
            state.ChunkIndex++;
            state.ByteCount += _chunks[i].Length;
            acc += route(_chunks[i], SinkCount, state);
        }

        return acc;
    }
}

[MemoryDiagnoser]
public class BroadcastStrategyBenchmarks
{
    private const int ChunkCount = 4_096;

    private const int ChunkSize = 4_096;

    private static readonly StrategyRegistry Registry = StrategyRegistry.Default();

    [Params(2, 8, 32)]
    public int SinkCount;

    private byte[][] _chunks = Array.Empty<byte[]>();
    private StrategyEntry _entry = null!;

    [GlobalSetup]
    public void Setup()
    {
        _chunks = ChunkGenerator.Build(ChunkCount, ChunkSize);
        if (!Registry.TryGet("broadcast", out _entry))
        {
            throw new InvalidOperationException("strategy 'broadcast' not registered");
        }
    }

    [Benchmark]
    public int Broadcast()
    {
        var broadcast = _entry.Broadcast
            ?? throw new InvalidOperationException("strategy 'broadcast' has no broadcast delegate");
        var state = new RouteState
        {
            Seed = 0x9E3779B97F4A7C15UL,
        };
        var mask = new BitArray(SinkCount);
        var set = 0;
        for (var i = 0; i < _chunks.Length; i++)
        {
            mask.SetAll(false);
            broadcast(_chunks[i], SinkCount, state, mask);
            for (var s = 0; s < SinkCount; s++)
            {
                if (mask[s])
                {
                    set++;
                }
            }
        }

        return set;
    }
}

internal static class ChunkGenerator
{
    public static byte[][] Build(int chunkCount, int chunkSize)
    {
        var chunks = new byte[chunkCount][];
        var seed = 0x1234_5678U;
        for (var i = 0; i < chunkCount; i++)
        {
            var chunk = new byte[chunkSize];
            for (var j = 0; j < chunkSize; j++)
            {
                seed = unchecked((seed * 1664525U) + 1013904223U);
                chunk[j] = (byte)(seed >> 24);
            }

            chunks[i] = chunk;
        }

        return chunks;
    }
}
