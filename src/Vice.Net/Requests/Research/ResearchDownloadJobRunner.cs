using Vice.Jobs;
using Vice.Logging;
using Vice.Net.Http;
using Vice.Persistence;

namespace Vice.Net.Research;

internal sealed class ResearchDownloadJobRunner : IJobRunner
{
    private readonly ResearchSourceRegistry _registry;
    private readonly Func<HttpClient> _httpFactory;

    public ResearchDownloadJobRunner()
        : this(new ResearchSourceRegistry(), ResearchHttp.Create)
    {
    }

    internal ResearchDownloadJobRunner(ResearchSourceRegistry registry,
                                       Func<HttpClient> httpFactory)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
    }

    public bool CanHandle(JobKind kind) => kind == JobKind.Download;

    public async Task RunAsync(JobState job,
                               IProgress<JobProgress> progress,
                               CancellationToken ct)
    {
        var source = _registry.Resolve(job.Source);
        var format = ExtensionToFormat(Path.GetExtension(job.DestinationPath));

        using var http = _httpFactory();
        var target = await source.ResolveDownloadAsync(http, job.ResourceId, format, ct).ConfigureAwait(false);

        var reporter = new Progress<DownloadProgress>(p =>
        {
            progress.Report(new JobProgress(
                BytesDownloaded: p.BytesDownloaded,
                TotalBytes: p.TotalBytes,
                Label: $"{job.Source}/{job.ResourceId} -> {job.DestinationPath}"));
        });

        await DownloadResumableAsync(http,
                                     target.Uri,
                                     job.DestinationPath,
                                     job.BytesDownloaded,
                                     reporter,
                                     ct).ConfigureAwait(false);
    }

    private static async Task DownloadResumableAsync(HttpClient http,
                                                     Uri uri,
                                                     string destinationPath,
                                                     long recordedOffset,
                                                     IProgress<DownloadProgress> progress,
                                                     CancellationToken ct)
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

        var partial = $"{fullPath}.partial";
        var resumable = new ResumableHttpStream(http, uri);

        var startOffset = ResolveResumeOffset(partial, recordedOffset);
        if (startOffset > 0
            && !await resumable.SupportsResumeAsync(ct).ConfigureAwait(false))
        {
            startOffset = 0;
        }

        try
        {
            long written;
            var observed = new ExpectedLengthObserver(progress);
            await using (var file = new FileStream(partial,
                                                   FileMode.OpenOrCreate,
                                                   FileAccess.ReadWrite,
                                                   FileShare.None))
            {
                if (startOffset > 0)
                {
                    file.SetLength(startOffset);
                    file.Seek(startOffset, SeekOrigin.Begin);
                }
                else
                {
                    file.SetLength(0);
                    file.Seek(0, SeekOrigin.Begin);
                }

                await resumable.DownloadAsync(file, startOffset, observed, ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false);
                written = file.Length;
            }

            if (observed.ExpectedTotal is long expected
                && written != expected)
            {
                throw new InvalidDataException(
                    $"Download of '{uri}' is incomplete: wrote {written} bytes but the server advertised {expected}; refusing to promote a truncated file.");
            }

            File.Move(partial, fullPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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

public static class ResearchJobFactory
{
    [Vice.Composition.ViceJobRunner]
    public static IJobRunner ResearchDownload() => new ResearchDownloadJobRunner();
}
