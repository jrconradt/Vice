using Vice.Jobs;
using Vice.Logging;
using Vice.Net.Requests.Http;

namespace Vice.Research;

public sealed class ResearchDownloadJobRunner : IJobRunner
{
    public static readonly JobKind DownloadKind = JobKind.Custom("Download");

    public const string SOURCE_KEY = "source";
    public const string RESOURCE_ID_KEY = "resourceId";
    public const string DESTINATION_PATH_KEY = "destinationPath";
    public const string FORMAT_KEY = "format";
    public const string EXTENSION_KEY = "extension";

    private readonly ResearchSourceResolver _resolveSource;
    private readonly Lazy<HttpClient> _http;
    private readonly IViceLogger _logger;

    public ResearchDownloadJobRunner(ResearchSourceResolver resolveSource,
                                     Func<HttpClient> httpFactory,
                                     IViceLogger? logger = null)
    {
        _resolveSource = resolveSource ?? throw new ArgumentNullException(nameof(resolveSource));
        ArgumentNullException.ThrowIfNull(httpFactory);
        _http = new Lazy<HttpClient>(httpFactory, LazyThreadSafetyMode.ExecutionAndPublication);
        _logger = logger ?? NullViceLogger.Instance;
    }

    public static JobDescriptor DescriptorFor(string source,
                                              string resourceId,
                                              string destinationPath,
                                              string extension,
                                              string? format,
                                              IReadOnlyDictionary<string, string?>? extraOptions = null)
    {
        var options = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [SOURCE_KEY] = source,
            [RESOURCE_ID_KEY] = resourceId,
            [DESTINATION_PATH_KEY] = destinationPath,
            [EXTENSION_KEY] = extension,
            [FORMAT_KEY] = format,
        };

        if (extraOptions is not null)
        {
            foreach (var pair in extraOptions)
            {
                options[pair.Key] = pair.Value;
            }
        }

        return new JobDescriptor(DownloadKind,
                                 $"{source}/{resourceId}",
                                 options);
    }

    public bool CanHandle(JobKind kind) => kind == DownloadKind;

    public async Task RunAsync(JobDescriptor descriptor,
                               CancellationToken ct)
    {
        var sourceName = Option(descriptor, SOURCE_KEY);
        var resourceId = Option(descriptor, RESOURCE_ID_KEY);
        var destinationPath = Option(descriptor, DESTINATION_PATH_KEY);
        var carriedFormat = NullableOption(descriptor, FORMAT_KEY);

        var source = _resolveSource(sourceName);
        var format = carriedFormat ?? ExtensionToFormat(Path.GetExtension(destinationPath));

        var http = _http.Value;

        DownloadTarget target;
        try
        {
            target = await source.ResolveDownloadAsync(http, resourceId, format, ct, _logger).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Log(ViceLogLevel.Warn,
                        $"research download could not resolve upstream for {sourceName}/{resourceId}",
                        ex);
            throw;
        }

        _logger.Log(ViceLogLevel.Debug,
                    $"research download resolved {sourceName}/{resourceId} to {target.Uri} -> {destinationPath}");

        try
        {
            var written = await ResumableHttpDownload.ToFileAsync(http,
                                                                  target.Uri,
                                                                  destinationPath,
                                                                  null,
                                                                  _logger,
                                                                  ct).ConfigureAwait(false);
            _logger.Log(ViceLogLevel.Debug,
                        $"research download completed {sourceName}/{resourceId} from {target.Uri}: wrote {written} bytes to {destinationPath}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Log(ViceLogLevel.Warn,
                        $"research download failed {sourceName}/{resourceId} from {target.Uri} -> {destinationPath}",
                        ex);
            throw;
        }
    }

    private static string Option(JobDescriptor descriptor, string key)
    {
        return descriptor.Options.TryGetValue(key, out var value) && value is not null
            ? value
            : string.Empty;
    }

    private static string? NullableOption(JobDescriptor descriptor, string key)
    {
        if (descriptor.Options.TryGetValue(key, out var value)
            && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        return null;
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
    public static ResearchDownloadJobRunner ResearchDownload(ResearchHttpService http)
    {
        return new ResearchDownloadJobRunner(ResearchSources.Resolve,
                                             () => http.Client);
    }
}
