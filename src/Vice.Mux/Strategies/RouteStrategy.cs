using System.Collections;

namespace Vice.Mux.Strategies;

public delegate int RouteStrategy(ReadOnlySpan<byte> chunk, int sinkCount, RouteState state);

public delegate void BroadcastStrategy(ReadOnlySpan<byte> chunk, int sinkCount, RouteState state, BitArray mask);

public sealed class RouteState
{
    public long ChunkIndex;
    public long ByteCount;
    public uint Cursor;
    public ulong Seed;
    public ReadOnlyMemory<int> Weights = ReadOnlyMemory<int>.Empty;
    public int KeyOffset;
    public int KeyLength = 4;
}
