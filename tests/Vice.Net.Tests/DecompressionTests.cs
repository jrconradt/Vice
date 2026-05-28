using System.IO.Compression;
using System.Text;
using Vice.Network.gRPC;
using Vice.Network.gRPC.Compression;
using Xunit;

namespace Vice.Net.Tests;

public class DecompressionTests
{
    private static string TempFile(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vice-dec-{Guid.NewGuid():N}{suffix}");
        return path;
    }

    [Fact]
    public async Task OpenReadStream_GzippedShortString_RoundTripsContent()
    {
        var path = TempFile(".gz");
        try
        {
            var payload = Encoding.UTF8.GetBytes("hello vice decompression");

            await using (var fs = File.Create(path))
            await using (var gz = new GZipStream(fs, CompressionLevel.Optimal, leaveOpen: false))
            {
                await gz.WriteAsync(payload);
            }

            await using var src = Decompression.OpenReadStream(path);
            using var ms = new MemoryStream();
            await src.CopyToAsync(ms);

            Assert.Equal(payload, ms.ToArray());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task OpenReadStream_GzipBomb_ThrowsInvalidDataException()
    {
        var path = TempFile(".gz");
        try
        {
            const int SIZE_BYTES = 8 * 1024 * 1024;
            var zeros = new byte[SIZE_BYTES];

            await using (var fs = File.Create(path))
            await using (var gz = new GZipStream(fs, CompressionLevel.SmallestSize, leaveOpen: false))
            {
                await gz.WriteAsync(zeros);
            }

            var compressedLen = new FileInfo(path).Length;
            Assert.True(compressedLen < SIZE_BYTES / 4,
                $"expected highly-compressible payload; compressed={compressedLen}, raw={SIZE_BYTES}");

            var cap = 1024L;

            await using var src = Decompression.OpenReadStream(path, cap);
            using var ms = new MemoryStream();

            var ex = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                await src.CopyToAsync(ms);
            });

            Assert.Contains("exceeded cap", ex.Message);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task OpenReadStream_GzipWithGarbageDeflateBody_Throws()
    {
        var path = TempFile(".gz");
        try
        {
            var header = new byte[]
            {
                0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF
            };
            await File.WriteAllBytesAsync(path, header);

            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var src = Decompression.OpenReadStream(path);
                using var ms = new MemoryStream();
                await src.CopyToAsync(ms);
            });
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task OpenReadStream_CorruptedGzipPayload_Throws()
    {
        var path = TempFile(".gz");
        try
        {
            await using (var fs = File.Create(path))
            await using (var gz = new GZipStream(fs, CompressionLevel.Optimal, leaveOpen: false))
            {
                var data = Encoding.UTF8.GetBytes("vice corruption test payload");
                await gz.WriteAsync(data);
            }

            var bytes = await File.ReadAllBytesAsync(path);
            for (var i = 10; i < bytes.Length - 8; i++)
            {
                bytes[i] ^= 0xFF;
            }

            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var src = Decompression.OpenReadStream(path);
                using var ms = new MemoryStream();
                await src.CopyToAsync(ms);
            });
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task OpenReadStream_GarbageGzip_Throws()
    {
        var path = TempFile(".gz");
        try
        {
            var rng = new Random(42);
            var garbage = new byte[256];
            rng.NextBytes(garbage);
            garbage[0] = 0x1F;
            garbage[1] = 0x8B;
            await File.WriteAllBytesAsync(path, garbage);

            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var src = Decompression.OpenReadStream(path);
                using var ms = new MemoryStream();
                await src.CopyToAsync(ms);
            });
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void OpenReadStream_NegativeMaxBytes_Throws()
    {
        var path = TempFile(".gz");
        Assert.Throws<ArgumentOutOfRangeException>(() => Decompression.OpenReadStream(path, -1));
    }

    [Fact]
    public void IsStreamCodec_RecognizesSupportedExtensions()
    {
        Assert.True(Decompression.IsStreamCodec("a.gz"));
        Assert.True(Decompression.IsStreamCodec("a.br"));
        Assert.True(Decompression.IsStreamCodec("a.deflate"));
        Assert.False(Decompression.IsStreamCodec("a.zip"));
        Assert.False(Decompression.IsStreamCodec("a"));
    }
}
