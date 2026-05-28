using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace Vice.Net.Http;

public sealed class ResumableHttpStream
{
    private readonly HttpClient _http;
    private readonly Uri _uri;
    private readonly long _maxBytes;
    private readonly Lazy<Task<ProbeResult>> _probeLazy;

    private sealed record ProbeResult(bool SupportsRange, long? TotalLength);

    public ResumableHttpStream(HttpClient http, Uri uri)
        : this(http, uri, maxBytes: null)
    {
    }

    public ResumableHttpStream(HttpClient http, Uri uri, long? maxBytes)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _uri = uri ?? throw new ArgumentNullException(nameof(uri));
        _maxBytes = maxBytes ?? HttpStreamHelper.MAX_DOWNLOAD_BYTES;
        if (_maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "maxBytes must be positive.");
        }

        _probeLazy = new Lazy<Task<ProbeResult>>(ProbeAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private async Task<ProbeResult> ProbeAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, _uri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var supportsRange = response.Headers.AcceptRanges.Contains("bytes");
        var totalLength = response.Content.Headers.ContentLength;

        if (totalLength.HasValue && totalLength.Value > _maxBytes)
        {
            throw new InvalidDataException(
                $"HEAD reported Content-Length {totalLength.Value} exceeding cap {_maxBytes}.");
        }

        return new ProbeResult(supportsRange, totalLength);
    }

    public async Task<bool> SupportsResumeAsync(CancellationToken ct)
    {
        var probe = await _probeLazy.Value.WaitAsync(ct).ConfigureAwait(false);
        return probe.SupportsRange;
    }

    public async Task DownloadAsync(
        Stream destination, long startOffset,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var probe = await _probeLazy.Value.WaitAsync(ct).ConfigureAwait(false);
        var supportsRange = probe.SupportsRange;
        var totalLength = probe.TotalLength;

        if (totalLength.HasValue && totalLength.Value > _maxBytes)
        {
            throw new InvalidDataException(
                $"Total length {totalLength.Value} exceeds cap {_maxBytes}.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);

        bool attemptingResume = startOffset > 0 && supportsRange;
        if (attemptingResume)
        {
            request.Headers.Range = new RangeHeaderValue(startOffset, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            var total = totalLength ?? startOffset;
            progress?.Report(new DownloadProgress(total, total));
            return;
        }

        response.EnsureSuccessStatusCode();

        long effectiveOffset;
        long? totalBytes;

        if (attemptingResume && response.StatusCode == HttpStatusCode.PartialContent)
        {
            effectiveOffset = startOffset;
            totalBytes = totalLength
                         ?? (response.Content.Headers.ContentLength.HasValue
                             ? response.Content.Headers.ContentLength.Value + startOffset
                             : null);
        }
        else
        {
            effectiveOffset = 0;
            totalBytes = response.Content.Headers.ContentLength ?? totalLength;

            if (attemptingResume)
            {
                if (!destination.CanSeek)
                {
                    throw new InvalidOperationException(
                        "Server ignored Range request and returned the full payload, but the " +
                        "destination stream is not seekable; cannot safely complete the resume. " +
                        "Retry with a seekable destination (e.g., FileStream).");
                }
                destination.Seek(0, SeekOrigin.Begin);
                destination.SetLength(0);
            }
        }

        if (totalBytes.HasValue && totalBytes.Value > _maxBytes)
        {
            throw new InvalidDataException(
                $"Response total bytes {totalBytes.Value} exceeds cap {_maxBytes}.");
        }

        await CopyToStreamAsync(response, destination, effectiveOffset, totalBytes, _maxBytes, progress, ct);
    }

    private static async Task CopyToStreamAsync(
        HttpResponseMessage response, Stream destination,
        long offset, long? totalBytes, long cap,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var source = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[HttpStreamHelper.BUFFER_SIZE];
        long bytesDownloaded = 0;
        var lastReport = Stopwatch.GetTimestamp();
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
        {
            bytesDownloaded += bytesRead;

            if (bytesDownloaded + offset > cap)
            {
                throw new InvalidDataException(
                    $"Download exceeded cap {cap} bytes mid-stream.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);

            if (progress is not null)
            {
                var elapsed = Stopwatch.GetElapsedTime(lastReport);
                if (elapsed >= HttpStreamHelper.ProgressThrottle)
                {
                    progress.Report(new DownloadProgress(bytesDownloaded + offset, totalBytes));
                    lastReport = Stopwatch.GetTimestamp();
                }
            }
        }

        progress?.Report(new DownloadProgress(bytesDownloaded + offset, totalBytes));
    }
}
