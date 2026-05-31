using System.Buffers;
using BenchmarkDotNet.Attributes;
using Vice.Mux.Routing;

namespace Vice.Benchmarks;

[MemoryDiagnoser]
public class BufferedIoBenchmarks
{
    private const int FileBufferSize = 81920;

    [Params(1 << 20, 8 << 20)]
    public int PayloadBytes;

    [Params(16384, 65536)]
    public int ChunkSize;

    private byte[] _payload = Array.Empty<byte>();
    private string _sourcePath = "";
    private string _destPath = "";

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
    public async Task<int> RouteOverMemoryStream()
    {
        using var input = new MemoryStream(_payload, writable: false);
        var clauses = new[]
        {
            new RouteClause(Condition.Any, $"file:{_destPath}"),
        };

        return await Router.RouteAsync(0, clauses, input, ChunkSize, CancellationToken.None).ConfigureAwait(false);
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
}
