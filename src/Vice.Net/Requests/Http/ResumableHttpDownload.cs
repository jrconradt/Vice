using System.Security.Cryptography;
using Vice.Concurrency;
using Vice.Logging;
using Vice.Persistence;

namespace Vice.Net.Requests.Http;

public static class ResumableHttpDownload
{
    public const int MAX_RESUME_ATTEMPTS = 3;

    private static readonly TimeSpan BaseResumeBackoff = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxResumeBackoff = TimeSpan.FromSeconds(30);

    public static Task<long> ToFileAsync(HttpClient http,
                                         Uri uri,
                                         string destinationPath,
                                         IProgress<DownloadProgress>? progress,
                                         IViceLogger logger,
                                         CancellationToken ct,
                                         long? maxBytes = null)
    {
        return ToFileAsync(http,
                           uri,
                           destinationPath,
                           recordedOffset: 0,
                           progress,
                           logger,
                           ct,
                           maxBytes);
    }

    public static async Task<long> ToFileAsync(HttpClient http,
                                               Uri uri,
                                               string destinationPath,
                                               long recordedOffset,
                                               IProgress<DownloadProgress>? progress,
                                               IViceLogger logger,
                                               CancellationToken ct,
                                               long? maxBytes = null)
    {
        var fullPath = ResolveDestination(destinationPath);
        var partial = $"{fullPath}.partial";
        var resumable = maxBytes is long cap
            ? new ResumableHttpStream(http, uri, cap)
            : new ResumableHttpStream(http, uri);

        var attempt = 0;
        while (true)
        {
            try
            {
                var startOffset = attempt == 0
                    ? ResolveResumeOffset(partial, recordedOffset)
                    : ResolvePartialLength(partial);
                if (startOffset > 0
                    && !await resumable.SupportsResumeAsync(ct).ConfigureAwait(false))
                {
                    startOffset = 0;
                }

                if (startOffset > 0)
                {
                    logger.Log(ViceLogLevel.Debug,
                               $"resumable download resuming {uri} at byte offset {startOffset}");
                }

                return await PromoteAsync(resumable,
                                          uri,
                                          fullPath,
                                          partial,
                                          startOffset,
                                          progress,
                                          logger,
                                          ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException ex)
            {
                if (attempt >= MAX_RESUME_ATTEMPTS)
                {
                    SafeFile.TryDelete(partial);
                    logger.Log(ViceLogLevel.Error,
                               $"resumable download for {uri} exhausted {MAX_RESUME_ATTEMPTS} resume attempts",
                               ex);
                    throw;
                }

                attempt++;
                var backoff = RetryBackoff.Exponential(BaseResumeBackoff, MaxResumeBackoff, attempt);
                logger.Log(ViceLogLevel.Warn,
                           $"resumable download mid-stream failure for {uri} (resume {attempt}/{MAX_RESUME_ATTEMPTS} after {backoff.TotalMilliseconds:0}ms)",
                           ex);
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
            catch
            {
                SafeFile.TryDelete(partial);
                throw;
            }
        }
    }

    private static async Task<long> PromoteAsync(ResumableHttpStream resumable,
                                                 Uri uri,
                                                 string fullPath,
                                                 string partial,
                                                 long startOffset,
                                                 IProgress<DownloadProgress>? progress,
                                                 IViceLogger logger,
                                                 CancellationToken ct)
    {
        long written;
        var observed = new ExpectedLengthObserver(progress);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var file = new FileStream(partial,
                                               FileMode.OpenOrCreate,
                                               FileAccess.ReadWrite,
                                               FileShare.None))
        {
            file.SetLength(startOffset);
            file.Seek(startOffset, SeekOrigin.Begin);

            await resumable.DownloadAsync(file, startOffset, observed, ct, hash).ConfigureAwait(false);
            await file.FlushAsync(ct).ConfigureAwait(false);
            SafeFile.FlushToDisk(file.SafeFileHandle);
            written = file.Length;
        }

        if (observed.ExpectedTotal is not long expected)
        {
            throw new InvalidDataException(
                $"Download of '{uri}' cannot be verified: the server advertised no Content-Length or Content-Range, so completion is indeterminate; refusing to promote an unverifiable file.");
        }

        if (written != expected)
        {
            throw new InvalidDataException(
                $"Download of '{uri}' is incomplete: wrote {written} bytes but the server advertised {expected}; refusing to promote a truncated file.");
        }

        var digest = Convert.ToHexStringLower(hash.GetHashAndReset());

        File.Move(partial, fullPath, overwrite: true);
        var promotedDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(promotedDir))
        {
            SafeFile.FlushDirectory(promotedDir);
        }

        logger.Log(ViceLogLevel.Info,
                   $"download integrity sha256={digest} bytes={written} for '{uri}' -> {fullPath}");

        return written;
    }

    private static string ResolveDestination(string destinationPath)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        if (!SafeWriteRoots.IsAllowed(fullPath, out var reason))
        {
            throw new BadArgument($"Destination '{fullPath}' is outside allowed write roots: {reason}.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    private static long ResolveResumeOffset(string partial,
                                            long recordedOffset)
    {
        var info = new FileInfo(partial);
        if (!info.Exists)
        {
            return 0;
        }

        if (recordedOffset <= 0)
        {
            return Math.Max(0, info.Length);
        }

        return Math.Min(info.Length, recordedOffset);
    }

    private static long ResolvePartialLength(string partial)
    {
        var info = new FileInfo(partial);
        if (!info.Exists)
        {
            return 0;
        }

        return Math.Max(0, info.Length);
    }

    private sealed class ExpectedLengthObserver : IProgress<DownloadProgress>
    {
        private readonly IProgress<DownloadProgress>? _inner;
        private long _expected = -1;

        public ExpectedLengthObserver(IProgress<DownloadProgress>? inner)
        {
            _inner = inner;
        }

        public long? ExpectedTotal => _expected >= 0 ? _expected : null;

        public void Report(DownloadProgress value)
        {
            if (value.TotalBytes is long total
                && total >= 0)
            {
                Volatile.Write(ref _expected, total);
            }

            _inner?.Report(value);
        }
    }
}
