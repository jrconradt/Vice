using System.Net;
using System.Text;
using System.Threading.Tasks;
using Vice.Net.Http;
using Xunit;

namespace Vice.Net.Tests;

public class SadPath_NetTests
{
    [Fact]
    public async Task HttpStreamHelper_ZeroByteResponse_Succeeds_WithEmptyStream()
    {
        await using var server = new HttpTestServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = 0;
            ctx.Response.Close();
            await Task.CompletedTask;
        });

        using var http = new HttpClient();
        using var dest = new MemoryStream();
        await HttpStreamHelper.DownloadToStreamAsync(http, server.BaseUrl + "x", dest,
            progress: null, ct: CancellationToken.None);

        Assert.Equal(0, dest.Length);
    }

    [Fact]
    public async Task ResumableHttpStream_ServerIgnoresRange_Returns200_TruncatesAndRewrites()
    {
        var payload = Encoding.UTF8.GetBytes("0123456789");

        await using var server = MisbehavingServer(payload);

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"));

        using var dest = new MemoryStream();
        dest.Write(payload, 0, 4);
        Assert.Equal(4, dest.Length);

        await rs.DownloadAsync(dest, startOffset: 4, progress: null, ct: CancellationToken.None);

        Assert.Equal(payload, dest.ToArray());
        Assert.Equal(payload.Length, dest.Length);
    }

    [Fact]
    public async Task ResumableHttpStream_ServerIgnoresRange_NonSeekableDestination_Throws()
    {
        var payload = Encoding.UTF8.GetBytes("0123456789");
        await using var server = MisbehavingServer(payload);

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"));

        var dest = new NonSeekableStream();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => rs.DownloadAsync(dest, startOffset: 4, progress: null, ct: CancellationToken.None));
    }

    private static HttpTestServer MisbehavingServer(byte[] payload) =>
        new(async ctx =>
        {
            ctx.Response.Headers["Accept-Ranges"] = "bytes";

            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        });

    private sealed class NonSeekableStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    [Fact]
    public async Task ResumableHttpStream_Head404_Throws()
    {
        await using var server = new HttpTestServer(async ctx =>
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            await Task.CompletedTask;
        });

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "missing"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => rs.SupportsResumeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ResumableHttpStream_SupportsResume_IsCached()
    {
        var payload = Encoding.UTF8.GetBytes("abc");
        var headCount = 0;
        await using var server = new HttpTestServer(async ctx =>
        {
            if (ctx.Request.HttpMethod == "HEAD")
            {
                Interlocked.Increment(ref headCount);
                ctx.Response.StatusCode = 200;
                ctx.Response.Headers["Accept-Ranges"] = "bytes";
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.Close();
                return;
            }
            ctx.Response.StatusCode = 200;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        });

        using var http = new HttpClient();
        var rs = new ResumableHttpStream(http, new Uri(server.BaseUrl + "x"));

        await rs.SupportsResumeAsync(CancellationToken.None);
        await rs.SupportsResumeAsync(CancellationToken.None);
        await rs.SupportsResumeAsync(CancellationToken.None);

        Assert.Equal(1, headCount);
    }
}
