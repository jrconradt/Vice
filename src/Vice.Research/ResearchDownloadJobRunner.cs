using Vice.Jobs;
using Vice.Logging;
using Vice.Net.Requests.Http;

namespace Vice.Research;

public sealed class ResearchDownloadJobRunner : IJobRunner
{
    private const int MAX_DOWNLOAD_RETRIES = 5;
    private static readonly TimeSpan BASE_RETRY_BACKOFF = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MAX_RETRY_BACKOFF = TimeSpan.FromSeconds(30);

    private readonly ResearchSourceRegistry _registry;
    private readonly Func<HttpClient> _httpFactory;
    private readonly IViceLogger _logger;

    public ResearchDownloadJobRunner()
        : this(new ResearchSourceRegistry(), ResearchHttp.Create)
    {
    }

    internal ResearchDownloadJobRunner(ResearchSourceRegistry registry,
                                       Func<HttpClient> httpFactory,
                                       IViceLogger? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        _logger = logger ?? NullViceLogger.Instance;
    }

    public bool CanHandle(JobKind kind) => kind == JobKind.Download;

    public async Task RunAsync(JobState job,
                               IProgress<JobProgress> progress,
                               CancellationToken ct)
    {
        var source = _registry.Resolve(job.Source);
        var format = job.Format ?? ExtensionToFormat(Path.GetExtension(job.DestinationPath));

        using var http = _httpFactory();

        DownloadTarget target;
        try
        {
            target = await source.ResolveDownloadAsync(http, job.ResourceId, format, ct, _logger).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Log(ViceLogLevel.Warn,
                        $"research download could not resolve upstream for {job.Source}/{job.ResourceId}",
                        ex);
            throw;
        }

        _logger.Log(ViceLogLevel.Debug,
                    $"research download resolved {job.Source}/{job.ResourceId} to {target.Uri} (resume offset {job.BytesDownloaded}) -> {job.DestinationPath}");

        var reporter = new Progress<DownloadProgress>(p =>
        {
            progress.Report(new JobProgress(
                BytesDownloaded: p.BytesDownloaded,
                TotalBytes: p.TotalBytes,
                Label: $"{job.Source}/{job.ResourceId} -> {job.DestinationPath}"));
        });

        try
        {
            var written = await DownloadResumableAsync(http,
                                                       target.Uri,
                                                       job.DestinationPath,
                                                       job.BytesDownloaded,
                                                       reporter,
                                                       ct,
                                                       _logger).ConfigureAwait(false);
            _logger.Log(ViceLogLevel.Debug,
                        $"research download completed {job.Source}/{job.ResourceId} from {target.Uri}: wrote {written} bytes to {job.DestinationPath}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Log(ViceLogLevel.Warn,
                        $"research download failed {job.Source}/{job.ResourceId} from {target.Uri} -> {job.DestinationPath}",
                        ex);
            throw;
        }
    }

    private static async Task<long> DownloadResumableAsync(HttpClient http,
                                                           Uri uri,
                                                           string destinationPath,
                                                           long recordedOffset,
                                                           IProgress<DownloadProgress> progress,
                                                           CancellationToken ct,
                                                           IViceLogger logger)
    {
        var fullPath = AtomicDownload.ResolveDestination(destinationPath);
        var partial = $"{fullPath}.partial";
        var resumable = new ResumableHttpStream(http, uri);

        var attempt = 0;
        while (true)
        {
            var startOffset = ResolveResumeOffset(partial, recordedOffset);
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

            try
            {
                return await AtomicDownload.RunAsync(resumable,
                                                     uri,
                                                     fullPath,
                                                     partial,
                                                     FileMode.OpenOrCreate,
                                                     startOffset,
                                                     progress,
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
                    logger.Log(ViceLogLevel.Error,
                                  $"research download for {uri} exhausted {MAX_DOWNLOAD_RETRIES} retries",
                                  ex);
                    throw new InvalidOperationException(
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
        var scaled = BASE_RETRY_BACKOFF.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(scaled, MAX_RETRY_BACKOFF.TotalMilliseconds);
        var jittered = capped * (0.5 + Random.Shared.NextDouble() * 0.5);
        return TimeSpan.FromMilliseconds(jittered);
    }

    private static long ResolveResumeOffset(string partial, long recordedOffset)
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

    private static string? ExtensionToFormat(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "" => null,
            "txt" => null,
            "pdf" => null,
            "fasta" => null,
            "xml" => "xml",
            "pdb" => null,
            "epub" => "epub",
            "html" => "html",
            "json" => "json",
            "gff" => "gff",
            "cif" => "cif",
            "bcif" => "bcif",
            _ => ext,
        };
    }
}

public static class ResearchJobFactory
{
    [Vice.Composition.ViceJobRunner]
    public static ResearchDownloadJobRunner ResearchDownload() => new ResearchDownloadJobRunner();
}
