using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace Vice.Net.Requests.Http;

public sealed class ResumableHttpStream
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly Uri _uri;
    private readonly long _maxBytes;
    private Lazy<Task<ProbeResult>>? _probe;

    private sealed record ProbeResult(bool SupportsRange, long? TotalLength, EntityTagHeaderValue? ETag, DateTimeOffset? LastModified);

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
    }

    private async Task<ProbeResult> GetProbeAsync(CancellationToken ct)
    {
        var existing = Volatile.Read(ref _probe);
        if (existing is not null && existing.IsValueCreated
            && (existing.Value.IsFaulted || existing.Value.IsCanceled))
        {
            Interlocked.CompareExchange(ref _probe, null, existing);
            existing = null;
        }

        if (existing is null)
        {
            var created = new Lazy<Task<ProbeResult>>(() => ProbeAsync(ct));
            existing = Interlocked.CompareExchange(ref _probe, created, null) ?? created;
        }

        try
        {
            return await existing.Value.ConfigureAwait(false);
        }
        catch
        {
            Interlocked.CompareExchange(ref _probe, null, existing);
            throw;
        }
    }

    private async Task<ProbeResult> ProbeAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProbeTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Head, _uri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var supportsRange = response.Headers.AcceptRanges.Contains("bytes");
        var totalLength = response.Content.Headers.ContentLength;
        var etag = response.Headers.ETag;
        var lastModified = response.Content.Headers.LastModified;

        if (totalLength.HasValue && totalLength.Value > _maxBytes)
        {
            throw new InvalidDataException(
                $"HEAD reported Content-Length {totalLength.Value} exceeding cap {_maxBytes}.");
        }

        return new ProbeResult(supportsRange, totalLength, etag, lastModified);
    }

    public async Task<bool> SupportsResumeAsync(CancellationToken ct)
    {
        var probe = await GetProbeAsync(ct).ConfigureAwait(false);
        return probe.SupportsRange;
    }

    public async Task DownloadAsync(
        Stream destination, long startOffset,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var probe = await GetProbeAsync(ct).ConfigureAwait(false);
        var supportsRange = probe.SupportsRange;
        var totalLength = probe.TotalLength;

        if (totalLength.HasValue && totalLength.Value > _maxBytes)
        {
            throw new InvalidDataException(
                $"Total length {totalLength.Value} exceeds cap {_maxBytes}.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);

        bool hasValidator = probe.ETag is not null || probe.LastModified.HasValue;
        bool attemptingResume = startOffset > 0 && supportsRange
            && hasValidator;
        if (attemptingResume)
        {
            request.Headers.Range = new RangeHeaderValue(startOffset, null);

            if (probe.ETag is not null)
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(probe.ETag);
            }
            else
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(probe.LastModified!.Value);
            }
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

        var contentRange = response.Content.Headers.ContentRange;

        if (attemptingResume
            && response.StatusCode == HttpStatusCode.PartialContent
            && !RangeStartsAt(contentRange, startOffset))
        {
            throw new InvalidDataException(
                $"Server returned 206 PartialContent with Content-Range '{contentRange}' " +
                $"that does not begin at the requested offset {startOffset}; refusing to " +
                "splice mismatched bytes.");
        }

        bool rangeHonored = attemptingResume && response.StatusCode == HttpStatusCode.PartialContent;

        if (rangeHonored)
        {
            effectiveOffset = startOffset;
            totalBytes = contentRange!.Length
                         ?? totalLength
                         ?? (response.Content.Headers.ContentLength.HasValue
                             ? response.Content.Headers.ContentLength.Value + startOffset
                             : null);
        }
        else
        {
            effectiveOffset = 0;
            totalBytes = response.Content.Headers.ContentLength ?? totalLength;

            if (startOffset > 0)
            {
                if (!destination.CanSeek)
                {
                    throw new InvalidOperationException(
                        "Server returned the full payload instead of the requested range, but the " +
                        "destination stream is not seekable; cannot safely restart the download. " +
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

    private static bool RangeStartsAt(ContentRangeHeaderValue? contentRange, long startOffset)
    {
        if (contentRange is null)
        {
            return false;
        }

        if (!contentRange.HasRange
            || contentRange.Unit != "bytes"
            || !contentRange.From.HasValue)
        {
            return false;
        }

        return contentRange.From.Value == startOffset;
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

        while (true)
        {
            int bytesRead;
            using (var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                stallCts.CancelAfter(StallTimeout);
                try
                {
                    bytesRead = await source.ReadAsync(buffer, stallCts.Token);
                }
                catch (OperationCanceledException) when (stallCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    throw new IOException(
                        $"Download stalled: no bytes received within {StallTimeout.TotalSeconds:0} seconds.");
                }
            }

            if (bytesRead <= 0)
            {
                break;
            }

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
