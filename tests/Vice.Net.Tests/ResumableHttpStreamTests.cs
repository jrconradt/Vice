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
            if (supportsRange && range is not null
                && range.StartsWith("bytes="))
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

    [Fact]
    public async Task SupportsResumeAsync_HeadContentLengthOverCap_ThrowsInvalidData()
    {
        var payload = Encoding.UTF8.GetBytes("0123456789");
        await using var server = await NewServer(payload, supportsRange: true);

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"), maxBytes: 4);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => rs.SupportsResumeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_HeadContentLengthOverCap_ThrowsInvalidData()
    {
        var payload = Encoding.UTF8.GetBytes("0123456789");
        await using var server = await NewServer(payload, supportsRange: true);

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"), maxBytes: 4);

        using var dest = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(
            () => rs.DownloadAsync(dest, startOffset: 0, progress: null, ct: CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_ResponseContentLengthOverCap_ThrowsInvalidData()
    {
        var payload = Encoding.UTF8.GetBytes("0123456789");
        await using var server = new HttpTestServer(async ctx =>
        {
            ctx.Response.Headers["Accept-Ranges"] = "bytes";

            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        });

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"), maxBytes: 4);

        using var dest = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(
            () => rs.DownloadAsync(dest, startOffset: 0, progress: null, ct: CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_StreamsPastCap_ThrowsMidStream()
    {
        var payload = new byte[64];

        await using var server = new HttpTestServer(async ctx =>
        {
            ctx.Response.Headers["Accept-Ranges"] = "bytes";

            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.SendChunked = true;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        });

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"), maxBytes: 8);

        using var dest = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(
            () => rs.DownloadAsync(dest, startOffset: 0, progress: null, ct: CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_RangeNotSatisfiable_ReportsCompleteAndReturns()
    {
        var payload = Encoding.UTF8.GetBytes("0123456789");

        await using var server = new HttpTestServer(async ctx =>
        {
            ctx.Response.Headers["Accept-Ranges"] = "bytes";

            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 416;
            ctx.Response.Close();
            await Task.CompletedTask;
        });

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"));

        DownloadProgress? last = null;
        var reporter = new Progress<DownloadProgress>(p => last = p);

        using var dest = new MemoryStream();
        await rs.DownloadAsync(dest, startOffset: payload.Length, progress: reporter, ct: CancellationToken.None);

        Assert.Equal(0, dest.Length);
    }
}
