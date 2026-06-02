using System.IO.Compression;
using CsCheck;
using Vice.Files;
using Xunit;

namespace Vice.Files.Tests;

public class DecompressionPropertyTests
{
    private const long ITERATIONS = 2_000;
    private const long CAP_BYTES = 64 * 1024;

    private static readonly string[] Codecs = ["gz", "br", "deflate"];

    private static readonly Gen<(string codec, byte[] bytes)> RandomCodecInput =
        from codec in Gen.OneOfConst(Codecs)
        from bytes in Gen.Byte.Array[0, 4096]
        select (codec, bytes);

    private static readonly Gen<(string codec, byte[] bytes)> ValidPrefixGarbageSuffix =
        from codec in Gen.OneOfConst(Codecs)
        from suffix in Gen.Byte.Array[1, 4096]
        select (codec, Concat(ValidPrefix(codec), suffix));

    private static byte[] Concat(byte[] head, byte[] tail)
    {
        var combined = new byte[head.Length + tail.Length];
        Buffer.BlockCopy(head, 0, combined, 0, head.Length);
        Buffer.BlockCopy(tail, 0, combined, head.Length, tail.Length);
        return combined;
    }

    private static byte[] ValidPrefix(string codec)
    {
        using var ms = new MemoryStream();
        Stream encoder = codec switch
        {
            "gz" => new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true),
            "br" => new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true),
            "deflate" => new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(codec))
        };

        using (encoder)
        {
            var payload = "vice valid prefix"u8.ToArray();
            encoder.Write(payload, 0, payload.Length);
        }

        return ms.ToArray();
    }

    private static void DrainExpectingOnlyGuardedFailures(string codec, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vice-dec-fuzz-{Guid.NewGuid():N}.{codec}");
        try
        {
            File.WriteAllBytes(path, bytes);

            try
            {
                using var src = Decompression.OpenReadStream(path, CAP_BYTES);
                using var sink = new MemoryStream();
                src.CopyTo(sink);

                Assert.True(sink.Length <= CAP_BYTES,
                    $"decompressed {sink.Length} bytes exceeded cap {CAP_BYTES}");
            }
            catch (Exception ex) when (ex is InvalidDataException
                                       or IOException)
            {
            }
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
    public void OpenReadStream_RandomBytes_OnlyGuardedFailuresEscapeAndCapHolds()
    {
        RandomCodecInput.Sample(input =>
            {
                var (codec, bytes) = input;
                DrainExpectingOnlyGuardedFailures(codec, bytes);
            },
            iter: ITERATIONS,
            seed: "0000DecFuzzRandom0");
    }

    [Fact]
    public void OpenReadStream_ValidPrefixGarbageSuffix_OnlyGuardedFailuresEscapeAndCapHolds()
    {
        ValidPrefixGarbageSuffix.Sample(input =>
            {
                var (codec, bytes) = input;
                DrainExpectingOnlyGuardedFailures(codec, bytes);
            },
            iter: ITERATIONS,
            seed: "0000DecFuzzPrefix0");
    }
}
