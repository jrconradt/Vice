using Vice.Net.Requests.Http;
using Xunit;

namespace Vice.Net.Tests;

public class DownloadProgressTests
{
    [Fact]
    public void Percentage_NullWhenTotalMissing()
    {
        var p = new DownloadProgress(100, null);
        Assert.Null(p.Percentage);
    }

    [Fact]
    public void Percentage_Computed_WhenTotalKnown()
    {
        var p = new DownloadProgress(50, 200);
        Assert.Equal(25.0, p.Percentage);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(3 * 1024 * 1024, "3.0 MB")]
    public void FormatSize_UnitScales(long bytes, string expected)
    {
        var p = new DownloadProgress(bytes, null);
        Assert.Equal(expected, p.FormatSize());
    }
}
