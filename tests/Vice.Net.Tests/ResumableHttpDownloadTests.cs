using System.Net;
using System.Text;
using Vice.Logging;
using Vice.Net.Requests.Http;
using Xunit;

namespace Vice.Net.Tests;

public sealed class ResumableHttpDownloadTests : IDisposable
{
    private const string ETAG = "\"resume-v1\"";

    private readonly string _tempDir;

    public ResumableHttpDownloadTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vice-research-resume-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
    }

    private static void WriteCommonHeaders(HttpListenerContext ctx)
    {
        ctx.Response.Headers["Accept-Ranges"] = "bytes";
        ctx.Response.Headers["ETag"] = ETAG;
    }

    private static long ParseRangeStart(string? range)
    {
        if (range is null || !range.StartsWith("bytes="))
        {
            return -1;
        }

        var startStr = range.Substring("bytes=".Length).TrimEnd('-');
        return long.TryParse(startStr, out var start) ? start : -1;
    }

    [Fact]
    public async Task ToFileAsync_TransientMidBodyDrop_KeepsPartial_ThenCompletes()
    {
        var payload = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog 0123456789");
        var prefixLength = 20;
        var getCount = 0;
        var partialExistedOnRetry = false;
        var partialLengthOnRetry = -1L;
        var destination = Path.Combine(_tempDir, "transient.txt");
        var partialPath = $"{destination}.partial";

        await using var server = new HttpTestServer(async ctx =>
        {
            WriteCommonHeaders(ctx);

            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.Close();
                return;
            }

            var attempt = Interlocked.Increment(ref getCount);
            if (attempt == 1)
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = payload.Length;
                await ctx.Response.OutputStream.WriteAsync(payload.AsMemory(0, prefixLength));
                await ctx.Response.OutputStream.FlushAsync();
                ctx.Response.OutputStream.Close();
                ctx.Response.Close();
                return;
            }

            partialExistedOnRetry = File.Exists(partialPath);
            partialLengthOnRetry = partialExistedOnRetry ? new FileInfo(partialPath).Length : -1;

            var range = ctx.Request.Headers["Range"];
            var start = ParseRangeStart(range);
            if (start > 0 && start < payload.Length)
            {
                var len = payload.Length - (int)start;
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["Content-Range"] = $"bytes {start}-{payload.Length - 1}/{payload.Length}";
                ctx.Response.ContentLength64 = len;
                await ctx.Response.OutputStream.WriteAsync(payload.AsMemory((int)start, len));
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        });

        using var http = new HttpClient();
        var written = await ResumableHttpDownload.ToFileAsync(http,
                                                                  new Uri(server.BaseUrl + "x"),
                                                                  destination,
                                                                  progress: null,
                                                                  logger: NullViceLogger.Instance,
                                                                  ct: CancellationToken.None);

        Assert.True(partialExistedOnRetry);
        Assert.Equal(prefixLength, partialLengthOnRetry);
        Assert.Equal(payload.Length, written);
        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(partialPath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task ToFileAsync_RecordedOffset_SendsRangeFromPartialLength_AndCompletes()
    {
        var payload = Encoding.UTF8.GetBytes("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        var prefixLength = 10;
        var observedRangeStart = -1L;
        var destination = Path.Combine(_tempDir, "recorded.txt");
        var partialPath = $"{destination}.partial";

        await File.WriteAllBytesAsync(partialPath, payload.AsMemory(0, prefixLength).ToArray());

        await using var server = new HttpTestServer(async ctx =>
        {
            WriteCommonHeaders(ctx);

            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.Close();
                return;
            }

            var start = ParseRangeStart(ctx.Request.Headers["Range"]);
            Interlocked.Exchange(ref observedRangeStart, start);
            if (start > 0 && start < payload.Length)
            {
                var len = payload.Length - (int)start;
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["Content-Range"] = $"bytes {start}-{payload.Length - 1}/{payload.Length}";
                ctx.Response.ContentLength64 = len;
                await ctx.Response.OutputStream.WriteAsync(payload.AsMemory((int)start, len));
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        });

        using var http = new HttpClient();
        var written = await ResumableHttpDownload.ToFileAsync(http,
                                                                  new Uri(server.BaseUrl + "x"),
                                                                  destination,
                                                                  recordedOffset: prefixLength,
                                                                  progress: null,
                                                                  logger: NullViceLogger.Instance,
                                                                  ct: CancellationToken.None);

        Assert.Equal(prefixLength, Volatile.Read(ref observedRangeStart));
        Assert.Equal(payload.Length, written);
        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(partialPath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task ToFileAsync_FreshCall_FirstAttemptFromZero_RetryResumesViaRange()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJ");
        var prefixLength = 16;
        var firstAttemptRange = "absent-sentinel";
        var secondAttemptRangeStart = -1L;
        var getCount = 0;
        var destination = Path.Combine(_tempDir, "fresh-retry.txt");
        var partialPath = $"{destination}.partial";

        await using var server = new HttpTestServer(async ctx =>
        {
            WriteCommonHeaders(ctx);

            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.Close();
                return;
            }

            var attempt = Interlocked.Increment(ref getCount);
            if (attempt == 1)
            {
                firstAttemptRange = ctx.Request.Headers["Range"] ?? "absent-sentinel";
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = payload.Length;
                await ctx.Response.OutputStream.WriteAsync(payload.AsMemory(0, prefixLength));
                await ctx.Response.OutputStream.FlushAsync();
                ctx.Response.OutputStream.Close();
                ctx.Response.Close();
                return;
            }

            var start = ParseRangeStart(ctx.Request.Headers["Range"]);
            Interlocked.Exchange(ref secondAttemptRangeStart, start);
            if (start > 0 && start < payload.Length)
            {
                var len = payload.Length - (int)start;
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["Content-Range"] = $"bytes {start}-{payload.Length - 1}/{payload.Length}";
                ctx.Response.ContentLength64 = len;
                await ctx.Response.OutputStream.WriteAsync(payload.AsMemory((int)start, len));
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        });

        using var http = new HttpClient();
        var written = await ResumableHttpDownload.ToFileAsync(http,
                                                                  new Uri(server.BaseUrl + "x"),
                                                                  destination,
                                                                  progress: null,
                                                                  logger: NullViceLogger.Instance,
                                                                  ct: CancellationToken.None);

        Assert.Equal("absent-sentinel", firstAttemptRange);
        Assert.Equal(prefixLength, Volatile.Read(ref secondAttemptRangeStart));
        Assert.Equal(payload.Length, written);
        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(partialPath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }
}
