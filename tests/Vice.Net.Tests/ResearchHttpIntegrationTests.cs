using System.Net;
using System.Text;
using System.Text.Json;
using Vice.Jobs;
using Vice.Logging;
using Vice.Research;
using Xunit;

namespace Vice.Net.Tests;

public sealed class ResearchHttpIntegrationTests : IAsyncLifetime, IDisposable
{
    private readonly string _tempDir;
    private HttpTestServer? _server;
    private byte[] _payload = Array.Empty<byte>();

    public ResearchHttpIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vice-research-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public Task InitializeAsync()
    {
        _payload = Encoding.UTF8.GetBytes("the quick brown fox downloaded over the lazy proxy");
        _server = new HttpTestServer(HandleAsync);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
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

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url!.AbsolutePath;
        if (path.StartsWith("/meta/", StringComparison.Ordinal))
        {
            var id = path["/meta/".Length..];
            var blobUrl = $"{_server!.BaseUrl}blob/{id}.txt";
            var body = Encoding.UTF8.GetBytes(
                $$"""{"id":"{{id}}","title":"Document {{id}}","downloadUrl":"{{blobUrl}}"}""");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = body.Length;
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
            return;
        }

        if (path.StartsWith("/blob/", StringComparison.Ordinal))
        {
            ctx.Response.Headers["Accept-Ranges"] = "bytes";
            if (ctx.Request.HttpMethod == "HEAD")
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = _payload.Length;
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = _payload.Length;
            await ctx.Response.OutputStream.WriteAsync(_payload);
            ctx.Response.Close();
            return;
        }

        ctx.Response.StatusCode = 404;
        ctx.Response.Close();
    }

    [Fact]
    public async Task Registry_Resolves_RegisteredSource_AndRoundTrips_MetadataThenDownload()
    {
        var source = new LoopbackResearchSource(_server!.BaseUrl);
        var registry = new ResearchSourceRegistry(new IResearchSource[] { source });

        var resolved = registry.Resolve("loopback");
        Assert.Same(source, resolved);

        using var http = new HttpClient();
        var target = await resolved.ResolveDownloadAsync(http, "doc-7", null, CancellationToken.None, NullViceLogger.Instance);

        Assert.Equal("txt", target.Extension);
        Assert.Equal($"{_server.BaseUrl}blob/doc-7.txt", target.Uri.AbsoluteUri);

        var destination = Path.Combine(_tempDir, "doc-7.txt");
        var written = await ResearchDownloader.DownloadToFileAsync(http,
                                                                  target.Uri,
                                                                  destination,
                                                                  progress: null,
                                                                  logger: NullViceLogger.Instance,
                                                                  ct: CancellationToken.None);

        Assert.Equal(_payload.Length, written);
        Assert.True(File.Exists(destination));
        Assert.Equal(_payload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public void Registry_ResolveByAlias_ReturnsSameSource()
    {
        var source = new LoopbackResearchSource(_server!.BaseUrl);
        var registry = new ResearchSourceRegistry(new IResearchSource[] { source });

        var byName = registry.Resolve("loopback");
        var byAlias = registry.Resolve("lb");

        Assert.Same(byName, byAlias);
    }

    [Fact]
    public async Task Downloader_FetchAsync_ParsesMetadataFromLiveServer()
    {
        var source = new LoopbackResearchSource(_server!.BaseUrl);

        using var http = new HttpClient();
        var fetched = await source.FetchAsync(http, "doc-42", CancellationToken.None);

        Assert.Equal("doc-42", fetched.Id);
        Assert.Equal("Document doc-42", fetched.Title);
        Assert.Contains($"Download: {_server.BaseUrl}blob/doc-42.txt", fetched.MetadataLines);
    }

    [Fact]
    public void JobRunner_CanHandle_OnlyDownloadKind()
    {
        var registry = new ResearchSourceRegistry(new IResearchSource[]
                                                  {
                                                      new LoopbackResearchSource(_server!.BaseUrl),
                                                  });
        var runner = new ResearchDownloadJobRunner(registry, () => new HttpClient());

        Assert.True(runner.CanHandle(JobKind.Download));
        Assert.False(runner.CanHandle(JobKind.GrpcStream));
    }

    [Fact]
    public async Task JobRunner_HappyPath_ResolvesAndWritesDestination()
    {
        var registry = new ResearchSourceRegistry(new IResearchSource[]
                                                  {
                                                      new LoopbackResearchSource(_server!.BaseUrl),
                                                  });
        var runner = new ResearchDownloadJobRunner(registry, () => new HttpClient());
        var destination = Path.Combine(_tempDir, "job-output.txt");

        var job = new JobState
        {
            Id = 11,
            Kind = JobKind.Download,
            Source = "loopback",
            ResourceId = "doc-99",
            DestinationPath = destination,
        };

        await runner.RunAsync(job, new Progress<JobProgress>(_ => { }), CancellationToken.None);

        Assert.True(File.Exists(destination));
        Assert.Equal(_payload, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists($"{destination}.partial"));
    }

    [Fact]
    public async Task JobRunner_FailedDownload_DoesNotPromotePartial()
    {
        var registry = new ResearchSourceRegistry(new IResearchSource[]
                                                  {
                                                      new LoopbackResearchSource(_server!.BaseUrl, downloadHostOverride: "no-such-host.invalid"),
                                                  });
        var runner = new ResearchDownloadJobRunner(registry, () => new HttpClient());
        var destination = Path.Combine(_tempDir, "job-fail.txt");

        var job = new JobState
        {
            Id = 12,
            Kind = JobKind.Download,
            Source = "loopback",
            ResourceId = "doc-1",
            DestinationPath = destination,
        };

        var exhausted = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(job, new Progress<JobProgress>(_ => { }), CancellationToken.None));
        Assert.IsType<HttpRequestException>(exhausted.InnerException);

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists($"{destination}.partial"));
    }

    [Fact]
    public async Task JobRunner_PrefersCarriedFormat_OverExtensionDerived()
    {
        var recorder = new FormatRecordingResearchSource(_server!.BaseUrl);
        var registry = new ResearchSourceRegistry(new IResearchSource[] { recorder });
        var runner = new ResearchDownloadJobRunner(registry, () => new HttpClient());
        var destination = Path.Combine(_tempDir, "carried.xml");

        var job = new JobState
        {
            Id = 21,
            Kind = JobKind.Download,
            Source = "recorder",
            ResourceId = "doc-5",
            DestinationPath = destination,
            Format = "epub",
        };

        await runner.RunAsync(job, new Progress<JobProgress>(_ => { }), CancellationToken.None);

        Assert.Equal("epub", recorder.LastFormat);
    }

    [Fact]
    public async Task JobRunner_NoCarriedFormat_FallsBackToExtension()
    {
        var recorder = new FormatRecordingResearchSource(_server!.BaseUrl);
        var registry = new ResearchSourceRegistry(new IResearchSource[] { recorder });
        var runner = new ResearchDownloadJobRunner(registry, () => new HttpClient());
        var destination = Path.Combine(_tempDir, "fallback.xml");

        var job = new JobState
        {
            Id = 22,
            Kind = JobKind.Download,
            Source = "recorder",
            ResourceId = "doc-6",
            DestinationPath = destination,
        };

        await runner.RunAsync(job, new Progress<JobProgress>(_ => { }), CancellationToken.None);

        Assert.Equal("xml", recorder.LastFormat);
    }

    private sealed class FormatRecordingResearchSource : IResearchSource
    {
        private readonly string _baseUrl;

        public FormatRecordingResearchSource(string baseUrl)
        {
            _baseUrl = baseUrl;
        }

        public string? LastFormat
        {
            get;
            private set;
        }

        public string Name => "recorder";

        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        public bool Searchable => false;

        public string DefaultExtension => "txt";

        public Task<IReadOnlyList<SearchHit>> SearchAsync(HttpClient http,
                                                          string query,
                                                          int limit,
                                                          int offset,
                                                          CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<FetchResult> FetchAsync(HttpClient http,
                                            string id,
                                            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<DownloadTarget> ResolveDownloadAsync(HttpClient http,
                                                         string id,
                                                         string? format,
                                                         CancellationToken ct,
                                                         IViceLogger logger)
        {
            LastFormat = format;
            var uri = new Uri($"{_baseUrl}blob/{id}.txt");
            return Task.FromResult(new DownloadTarget(uri, DefaultExtension));
        }
    }

    private sealed class LoopbackResearchSource : IResearchSource
    {
        private readonly string _baseUrl;
        private readonly string? _downloadHostOverride;

        public LoopbackResearchSource(string baseUrl,
                                      string? downloadHostOverride = null)
        {
            _baseUrl = baseUrl;
            _downloadHostOverride = downloadHostOverride;
        }

        public string Name => "loopback";

        public IReadOnlyList<string> Aliases => new[] { "lb" };

        public bool Searchable => true;

        public string DefaultExtension => "txt";

        public async Task<IReadOnlyList<SearchHit>> SearchAsync(HttpClient http,
                                                               string query,
                                                               int limit,
                                                               int offset,
                                                               CancellationToken ct)
        {
            await Task.Yield();
            return new[] { new SearchHit("doc-1", $"hit for {query}", "summary") };
        }

        public async Task<FetchResult> FetchAsync(HttpClient http,
                                                  string id,
                                                  CancellationToken ct)
        {
            var meta = await GetMetadataAsync(http, id, ct).ConfigureAwait(false);
            var title = meta.GetProperty("title").GetString() ?? string.Empty;
            var downloadUrl = meta.GetProperty("downloadUrl").GetString() ?? string.Empty;
            var lines = new List<string>
            {
                $"Download: {downloadUrl}",
            };
            return new FetchResult(id, title, lines, string.Empty);
        }

        public async Task<DownloadTarget> ResolveDownloadAsync(HttpClient http,
                                                              string id,
                                                              string? format,
                                                              CancellationToken ct,
                                                              IViceLogger logger)
        {
            var meta = await GetMetadataAsync(http, id, ct).ConfigureAwait(false);
            var downloadUrl = meta.GetProperty("downloadUrl").GetString()
                ?? throw new BadArgument($"loopback id '{id}' has no download url.");

            if (_downloadHostOverride is not null)
            {
                var bad = new UriBuilder(downloadUrl)
                {
                    Host = _downloadHostOverride,
                    Port = 9,
                };
                return new DownloadTarget(bad.Uri, DefaultExtension);
            }

            return new DownloadTarget(new Uri(downloadUrl), DefaultExtension);
        }

        private async Task<JsonElement> GetMetadataAsync(HttpClient http,
                                                         string id,
                                                         CancellationToken ct)
        {
            var url = $"{_baseUrl}meta/{id}";
            var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
    }
}
