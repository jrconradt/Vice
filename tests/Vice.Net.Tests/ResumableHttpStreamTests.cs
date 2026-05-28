using System.Net;
using System.Text;
using System.Threading.Tasks;
using Vice.Net.Http;
using Xunit;

namespace Vice.Net.Tests;

public class ResumableHttpStreamTests
{
    private static async Task<HttpTestServer> NewServer(byte[] payload, bool supportsRange)
    {
        var server = new HttpTestServer(async ctx =>
        {
            if (supportsRange)
            {
                ctx.Response.Headers["Accept-Ranges"] = "bytes";
            }

            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.Close();
                return;
            }

            var range = ctx.Request.Headers["Range"];
            if (supportsRange && range is not null && range.StartsWith("bytes="))
            {
                var startStr = range.Substring("bytes=".Length).TrimEnd('-');
                if (long.TryParse(startStr, out var start) && start < payload.Length)
                {
                    var len = payload.Length - (int)start;
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers["Content-Range"] = $"bytes {start}-{payload.Length - 1}/{payload.Length}";
                    ctx.Response.ContentLength64 = len;
                    await ctx.Response.OutputStream.WriteAsync(payload, (int)start, len);
                    ctx.Response.Close();
                    return;
                }
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        });
        await Task.Yield();
        return server;
    }

    [Fact]
    public async Task SupportsResumeAsync_True_WhenAcceptRangesIsBytes()
    {
        var payload = Encoding.UTF8.GetBytes("abcdef");
        await using var server = await NewServer(payload, supportsRange: true);

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"));

        Assert.True(await rs.SupportsResumeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SupportsResumeAsync_False_WhenAcceptRangesAbsent()
    {
        var payload = Encoding.UTF8.GetBytes("abcdef");
        await using var server = await NewServer(payload, supportsRange: false);

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"));

        Assert.False(await rs.SupportsResumeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_FromZero_CopiesFullPayload()
    {
        var payload = Encoding.UTF8.GetBytes("hello vice");
        await using var server = await NewServer(payload, supportsRange: true);

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"));

        using var dest = new MemoryStream();
        await rs.DownloadAsync(dest, startOffset: 0, progress: null, ct: CancellationToken.None);
        Assert.Equal(payload, dest.ToArray());
    }

    [Fact]
    public async Task DownloadAsync_FromOffset_FetchesTail_WhenSupported()
    {
        var payload = Encoding.UTF8.GetBytes("0123456789");
        await using var server = await NewServer(payload, supportsRange: true);

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"));

        using var dest = new MemoryStream();
        await rs.DownloadAsync(dest, startOffset: 4, progress: null, ct: CancellationToken.None);

        Assert.Equal(Encoding.UTF8.GetBytes("456789"), dest.ToArray());
    }
}
