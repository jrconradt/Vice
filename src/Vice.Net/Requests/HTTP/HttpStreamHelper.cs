using System.Diagnostics;
using Vice.Streaming;

namespace Vice.Net.Http;

public static class HttpStreamHelper
{
    public const int BUFFER_SIZE = BufferConstants.FILE_IO;

    public const long MAX_DOWNLOAD_BYTES = 8L * 1024 * 1024 * 1024;

    public static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(250);

    public static async Task DownloadToStreamAsync(
        HttpClient http, string url, Stream destination,
        IProgress<DownloadProgress>? progress, CancellationToken ct,
        long? maxBytes = null)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await CopyResponseToStreamAsync(response, destination, progress, maxBytes ?? MAX_DOWNLOAD_BYTES, ct);
    }

    public static async Task DownloadToStreamAsync(
        HttpClient http, Uri uri, Stream destination,
        IProgress<DownloadProgress>? progress, CancellationToken ct,
        long? maxBytes = null)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await CopyResponseToStreamAsync(response, destination, progress, maxBytes ?? MAX_DOWNLOAD_BYTES, ct);
    }

    public static async Task StreamChunksAsync(
        HttpClient http, Uri uri,
        IStreamContext<byte[]> stream,
        IProgress<DownloadProgress>? progress, CancellationToken ct,
        long? maxBytes = null)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        try
        {
            await PumpAsync(response, new YieldSink(stream), progress, maxBytes ?? MAX_DOWNLOAD_BYTES, ct);
        }
        finally
        {
            stream.Complete();
        }
    }

    private static Task CopyResponseToStreamAsync(
        HttpResponseMessage response, Stream destination,
        IProgress<DownloadProgress>? progress, long cap, CancellationToken ct)
        => PumpAsync(response, new StreamWriteSink(destination), progress, cap, ct);

    private static async Task PumpAsync(
        HttpResponseMessage response, IDownloadSink sink,
        IProgress<DownloadProgress>? progress, long cap, CancellationToken ct)
    {
        var totalBytes = response.Content.Headers.ContentLength;

        if (totalBytes.HasValue && totalBytes.Value > cap)
        {
            throw new InvalidDataException(
                $"Response Content-Length {totalBytes.Value} exceeds cap {cap}.");
        }

        using var source = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[BUFFER_SIZE];
        long bytesDownloaded = 0;
        var lastReport = Stopwatch.GetTimestamp();
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
        {
            bytesDownloaded += bytesRead;
            if (bytesDownloaded > cap)
            {
                throw new InvalidDataException(
                    $"Download exceeded cap {cap} bytes mid-stream.");
            }

            buffer = await sink.AcceptAsync(buffer, bytesRead, ct);

            if (progress is not null)
            {
                var elapsed = Stopwatch.GetElapsedTime(lastReport);
                if (elapsed >= ProgressThrottle)
                {
                    progress.Report(new DownloadProgress(bytesDownloaded, totalBytes));
                    lastReport = Stopwatch.GetTimestamp();
                }
            }
        }

        progress?.Report(new DownloadProgress(bytesDownloaded, totalBytes));
    }

    private interface IDownloadSink
    {
        ValueTask<byte[]> AcceptAsync(byte[] buffer, int bytesRead, CancellationToken ct);
    }

    private sealed class StreamWriteSink : IDownloadSink
    {
        private readonly Stream _destination;
        public StreamWriteSink(Stream destination) => _destination = destination;

        public async ValueTask<byte[]> AcceptAsync(byte[] buffer, int bytesRead, CancellationToken ct)
        {
            await _destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            return buffer;
        }
    }

    private sealed class YieldSink : IDownloadSink
    {
        private readonly IStreamContext<byte[]> _stream;
        public YieldSink(IStreamContext<byte[]> stream) => _stream = stream;

        public async ValueTask<byte[]> AcceptAsync(byte[] buffer, int bytesRead, CancellationToken ct)
        {
            byte[] chunk;
            byte[] next;
            if (bytesRead == buffer.Length)
            {
                chunk = buffer;
                next = new byte[BUFFER_SIZE];
            }
            else
            {
                chunk = new byte[bytesRead];
                buffer.AsSpan(0, bytesRead).CopyTo(chunk);
                next = buffer;
            }
            await _stream.YieldAsync(chunk, ct);
            return next;
        }
    }
}
