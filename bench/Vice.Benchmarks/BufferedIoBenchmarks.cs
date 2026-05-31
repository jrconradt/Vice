using System.Buffers;
using BenchmarkDotNet.Attributes;
using Vice.Mux.Sinks;
using Vice.Mux.Strategies;

namespace Vice.Benchmarks;

[MemoryDiagnoser]
public class BufferedIoBenchmarks
{
    private const int SinkCount = 4;

    private const int FileBufferSize = 81920;

    [Params(1 << 20, 8 << 20)]
    public int PayloadBytes;

    [Params(16384, 65536)]
    public int ChunkSize;

    private byte[] _payload = Array.Empty<byte>();
    private string _sourcePath = "";
    private string _destPath = "";
    private StrategyEntry _route = null!;

    [GlobalSetup]
    public void Setup()
    {
        _payload = new byte[PayloadBytes];
        var seed = 0xABCD_1234U;
        for (var i = 0; i < _payload.Length; i++)
        {
            seed = unchecked((seed * 1664525U) + 1013904223U);
            _payload[i] = (byte)(seed >> 24);
        }

        if (!StrategyRegistry.Default().TryGet("roundrobin", out _route))
        {
            throw new InvalidOperationException("strategy 'roundrobin' not registered");
        }

        _sourcePath = Path.Combine(Path.GetTempPath(), $"vice-bench-src-{Guid.NewGuid():N}.bin");
        _destPath = Path.Combine(Path.GetTempPath(), $"vice-bench-dst-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(_sourcePath, _payload);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        TryDelete(_sourcePath);
        TryDelete(_destPath);
    }

    [Benchmark]
    public async Task<long> SplitRouteOverMemoryStream()
    {
        using var input = new MemoryStream(_payload, writable: false);
        var sinks = new ISink[SinkCount];
        for (var i = 0; i < SinkCount; i++)
        {
            sinks[i] = new CountingSink();
        }

        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(ChunkSize);
        var state = new RouteState();
        var route = _route.Route
            ?? throw new InvalidOperationException("roundrobin has no route delegate");
        long routed = 0;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, ChunkSize)).ConfigureAwait(false)) > 0)
            {
                state.ChunkIndex++;
                state.ByteCount += read;
                var idx = route(buffer.AsSpan(0, read), SinkCount, state);
                await sinks[idx].WriteAsync(buffer.AsMemory(0, read), CancellationToken.None)
                    .ConfigureAwait(false);
                routed += read;
            }

            for (var i = 0; i < SinkCount; i++)
            {
                await sinks[i].FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            pool.Return(buffer);
            for (var i = 0; i < SinkCount; i++)
            {
                await sinks[i].DisposeAsync().ConfigureAwait(false);
            }
        }

        return routed;
    }

    [Benchmark]
    public async Task<long> BufferedFileCopy()
    {
        await using var source = new FileStream(
            _sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize,
            useAsync: true);
        await using var dest = new FileStream(
            _destPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            FileBufferSize,
            useAsync: true);

        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(FileBufferSize);
        long copied = 0;
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, FileBufferSize)).ConfigureAwait(false)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                copied += read;
            }

            await dest.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            pool.Return(buffer);
        }

        return copied;
    }

    private static void TryDelete(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class CountingSink : ISink
    {
        private long _bytes;

        public string Label => "count:";

        public long Bytes => _bytes;

        public ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            _bytes += chunk.Length;
            return ValueTask.CompletedTask;
        }

        public ValueTask FlushAsync(CancellationToken ct)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
