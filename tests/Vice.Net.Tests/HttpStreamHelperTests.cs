using System.Net;
using System.Text;
using System.Threading.Tasks;
using Vice.Net.Http;
using Xunit;

namespace Vice.Net.Tests;

public class HttpStreamHelperTests
{
    [Fact]
    public async Task DownloadToStreamAsync_CopiesAllBytes()
    {
        var payload = Encoding.UTF8.GetBytes("hello vice");

        await using var server = new HttpTestServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        });

        using var http = new HttpClient();
        using var dest = new MemoryStream();

        await HttpStreamHelper.DownloadToStreamAsync(http, server.BaseUrl + "x", dest,
            progress: null, ct: CancellationToken.None);

        Assert.Equal(payload, dest.ToArray());
    }

    [Fact]
    public async Task DownloadToStreamAsync_ReportsProgress()
    {
        var payload = new byte[200_000];
        new Random(42).NextBytes(payload);

        await using var server = new HttpTestServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        });

        using var http = new HttpClient();
        using var dest = new MemoryStream();

        var reports = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(p => { lock (reports) { reports.Add(p); } });

        await HttpStreamHelper.DownloadToStreamAsync(http, server.BaseUrl + "x", dest,
            progress, CancellationToken.None);

        Assert.Equal(payload.Length, dest.Length);
        await Task.Delay(60);
        Assert.NotEmpty(reports);

        Assert.Contains(reports, p => p.BytesDownloaded == payload.Length);
    }

    [Fact]
    public async Task DownloadToStreamAsync_PropagatesHttpError()
    {
        await using var server = new HttpTestServer(async ctx =>
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            await Task.CompletedTask;
        });

        using var http = new HttpClient();
        using var dest = new MemoryStream();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            HttpStreamHelper.DownloadToStreamAsync(http, server.BaseUrl + "missing", dest,
                null, CancellationToken.None));
    }
}
