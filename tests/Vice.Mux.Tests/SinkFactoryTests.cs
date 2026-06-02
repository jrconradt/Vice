using Vice.Logging;
using Vice.Mux.Sinks;
using Xunit;

namespace Vice.Mux.Tests;

public class SinkFactoryTests
{
    [Fact]
    public async Task FileSink_WritesAndDisposes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vice-mux-{Guid.NewGuid():N}.bin");
        try
        {
            await using (var sink = SinkFactory.Open($"file:{path}", NullViceLogger.Instance))
            {
                await sink.WriteAsync(new byte[] { 1, 2, 3, 4 }, default);
                await sink.FlushAsync(default);
            }
            var read = await File.ReadAllBytesAsync(path);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, read);
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
    public async Task AppendSink_AppendsToExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vice-mux-{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0xFF });
            await using (var sink = SinkFactory.Open($"append:{path}", NullViceLogger.Instance))
            {
                await sink.WriteAsync(new byte[] { 1, 2 }, default);
            }

            var read = await File.ReadAllBytesAsync(path);
            Assert.Equal(new byte[] { 0xFF, 1, 2 }, read);
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
    public async Task NullSink_AcceptsWritesSilently()
    {
        await using var sink = SinkFactory.Open("null:", NullViceLogger.Instance);
        await sink.WriteAsync(new byte[1024], default);
        await sink.FlushAsync(default);
        Assert.Equal("null:", sink.Label);
    }

    [Fact]
    public void UnknownScheme_Throws()
    {
        Assert.Throws<ArgumentException>(() => SinkFactory.Open("zzz:nope", NullViceLogger.Instance));
    }
}
