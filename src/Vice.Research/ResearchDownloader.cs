using Vice.Logging;
using Vice.Net.Requests.Http;
using Vice.Persistence;

namespace Vice.Research;

internal static class ResearchDownloader
{
    private const int MAX_DOWNLOAD_RETRIES = 5;
    private static readonly TimeSpan BaseRetryBackoff = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxRetryBackoff = TimeSpan.FromSeconds(30);

    public static Task<long> DownloadToFileAsync(HttpClient http,
                                                 Uri uri,
                                                 string destinationPath,
                                                 IProgress<DownloadProgress>? progress,
                                                 IViceLogger logger,
                                                 CancellationToken ct)
    {
        return DownloadToFileAsync(http,
                                   uri,
                                   destinationPath,
                                   recordedOffset: 0,
                                   progress,
                                   logger,
                                   ct);
    }

    public static async Task<long> DownloadToFileAsync(HttpClient http,
                                                       Uri uri,
                                                       string destinationPath,
                                                       long recordedOffset,
                                                       IProgress<DownloadProgress>? progress,
                                                       IViceLogger logger,
                                                       CancellationToken ct)
    {
        var fullPath = AtomicDownload.ResolveDestination(destinationPath);
        var partial = $"{fullPath}.partial";
        var resumable = new ResumableHttpStream(http, uri);

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
                               $"research download resuming {uri} at byte offset {startOffset}");
                }

                return await AtomicDownload.RunAsync(resumable,
                                                     uri,
                                                     fullPath,
                                                     partial,
                                                     FileMode.OpenOrCreate,
                                                     startOffset,
                                                     progress,
                                                     logger,
                                                     ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                if (attempt >= MAX_DOWNLOAD_RETRIES)
                {
                    SafeFile.TryDelete(partial);
                    logger.Log(ViceLogLevel.Error,
                               $"research download for {uri} exhausted {MAX_DOWNLOAD_RETRIES} retries",
                               ex);
                    throw new Vice.Jobs.NonRetryableJobException(
                        $"research download for {uri} exhausted {MAX_DOWNLOAD_RETRIES} retries: {ex.Message}",
                        ex);
                }

                attempt++;
                var backoff = ComputeBackoff(attempt);
                logger.Log(ViceLogLevel.Warn,
                           $"research download transient failure for {uri} (retry {attempt}/{MAX_DOWNLOAD_RETRIES} after {backoff.TotalMilliseconds:0}ms)",
                           ex);
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is HttpRequestException
            || ex is IOException
            || (ex is TaskCanceledException && ex.InnerException is TimeoutException);
    }

    private static TimeSpan ComputeBackoff(int attempt)
    {
        var scaled = BaseRetryBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(scaled, MaxRetryBackoff.TotalMilliseconds);
        var jittered = capped * (0.5 + Random.Shared.NextDouble() * 0.5);
        return TimeSpan.FromMilliseconds(jittered);
    }

    private static long ResolveResumeOffset(string partial,
                                            long recordedOffset)
    {
        if (recordedOffset <= 0)
        {
            return 0;
        }

        var info = new FileInfo(partial);
        if (!info.Exists)
        {
            return 0;
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

    public static string BuildUrlDestinationPath(string? toPath,
                                                 string fileName)
    {
        var safeName = Sanitize(fileName);
        if (string.IsNullOrWhiteSpace(toPath))
        {
            return Path.Combine(Environment.CurrentDirectory, safeName);
        }

        var full = Path.GetFullPath(toPath);
        if (Directory.Exists(full) || EndsWithSeparator(toPath))
        {
            return Path.Combine(full, safeName);
        }

        return full;
    }

    public static string BuildDestinationPath(string? toPath,
                                              string source,
                                              string id,
                                              string extension)
    {
        if (string.IsNullOrWhiteSpace(toPath))
        {
            return Path.Combine(Environment.CurrentDirectory, $"{Sanitize(id)}.{extension}");
        }

        var full = Path.GetFullPath(toPath);
        if (Directory.Exists(full) || EndsWithSeparator(toPath))
        {
            return Path.Combine(full, $"{Sanitize(id)}.{extension}");
        }

        return full;
    }

    private static bool EndsWithSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar);
    }

    private static string Sanitize(string id)
    {
        var chars = new List<char>(id.Length);
        foreach (var c in id)
        {
            if (char.IsLetterOrDigit(c) || c == '-'
                || c == '_'
                || c == '.')
            {
                chars.Add(c);
            }
            else
            {
                chars.Add('_');
            }
        }

        var start = 0;
        while (start < chars.Count && chars[start] == '.')
        {
            chars[start] = '_';
            start++;
        }

        var sanitized = new string(chars.ToArray());
        if (sanitized.Length == 0)
        {
            return "_";
        }

        return sanitized;
    }
}
