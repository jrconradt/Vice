using System.Collections.Concurrent;
using Vice.Composition;
using Vice.Execution;
using Vice.Jobs;
using Vice.Lexicon;
using Vice.Logging;
using Vice.Net.Http;
using Vice.Streaming;

namespace Vice.Net.Research;

[ViceCommandPack]
public static class ResearchCommands
{
    private static readonly ResearchSourceRegistry Sources = new();

    private static readonly Lazy<HttpClient> SharedHttp =
        new(ResearchHttp.Create, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<SearchHit>>>> InFlightSearches =
        new(StringComparer.Ordinal);

    private static HttpClient Http => SharedHttp.Value;

    public static void Register(IViceApp app)
    {
        app.RegisterStreaming<byte[]>(
            Verbs.Search() * Targets.Query > Connectors.On() > Nouns.Source() * Targets.Source,
            "Search a research source; piped emits one record per chunk, standalone prints a table",
            SearchAsStream,
            classicFallback: SearchToConsole);

        app.Register(
            Verbs.Fetch() * Targets.Id > Connectors.From() > Nouns.Source() * Targets.Source,
            "Fetch one result by id and print its metadata and a preview",
            FetchAsync);

        app.Register(
            Verbs.Download() * Targets.Id > Connectors.From() > Nouns.Source() * Targets.Source,
            "Download a result's content to the current directory",
            DownloadAsync);

        app.Register(
            Verbs.Download() * Targets.Id > Connectors.From() > Nouns.Source() * Targets.Source > Connectors.To() > Nouns.Path() * Targets.Path,
            "Download a result's content to a path",
            DownloadAsync);

        app.Register(
            Verbs.Download() * Targets.Id,
            "Download a raw http(s) URL to the current directory",
            RawDownloadAsync);

        app.Register(
            Verbs.Download() * Targets.Id > Connectors.To() > Nouns.Path() * Targets.Path,
            "Download a raw http(s) URL to a path",
            RawDownloadAsync);

        app.Register(
            Verbs.Archive() * Targets.Query > Connectors.From() > Nouns.Source() * Targets.Source,
            "Search a source and download every result to ./<source>-archive/",
            ArchiveAsync);

        app.Register(
            Verbs.Archive() * Targets.Query > Connectors.From() > Nouns.Source() * Targets.Source > Connectors.To() > Nouns.Path() * Targets.Path,
            "Search a source and download every result to a directory",
            ArchiveAsync);
    }

    private static async Task<int> SearchAsStream(IStreamingCommandContext<byte[]> ctx,
                                                  CancellationToken ct)
    {
        try
        {
            var (source, query) = ResolveSearch(ctx);
            var hits = await RunSearchAsync(ctx, source, query, ct).ConfigureAwait(false);
            foreach (var hit in hits)
            {
                var line = $"{hit.Id}\t{hit.Title}\n";
                await ctx.Stream.YieldAsync(System.Text.Encoding.UTF8.GetBytes(line), ct).ConfigureAwait(false);
            }

            ctx.Stream.Complete();
            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return CommandErrorHandler.HandleStreamingError(ctx, new TimedOut(ex));
        }
        catch (ViceError error)
        {
            return CommandErrorHandler.HandleStreamingError(ctx, error);
        }
        catch (HttpRequestException ex)
        {
            return CommandErrorHandler.HandleStreamingError(ctx, new HttpFailure(ex));
        }
        catch (Exception ex)
        {
            ctx.Stream.Fault(ex);
            throw;
        }
    }

    private static async Task<int> SearchToConsole(CommandContext ctx,
                                                   CancellationToken ct)
    {
        try
        {
            var (source, query) = ResolveSearch(ctx);
            var hits = await RunSearchAsync(ctx, source, query, ct).ConfigureAwait(false);
            if (hits.Count == 0)
            {
                Vice.Output.Line($"No results for '{query}' on {source.Name}.");
                return ViceExitCode.SUCCESS;
            }

            foreach (var hit in hits)
            {
                Vice.Output.Line($"{hit.Id,-16}  {Truncate(hit.Title, 70)}");
                if (hit.Summary.Length > 0)
                {
                    Vice.Output.Line($"                  {Truncate(hit.Summary, 70)}");
                }
            }

            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return CommandErrorHandler.Handle(ctx, new TimedOut(ex));
        }
        catch (ViceError error)
        {
            return CommandErrorHandler.Handle(ctx, error);
        }
        catch (HttpRequestException ex)
        {
            return CommandErrorHandler.Handle(ctx, new HttpFailure(ex));
        }
    }

    private static async Task<int> FetchAsync(CommandContext ctx,
                                              CancellationToken ct)
    {
        try
        {
            var source = Sources.Resolve(Require(ctx, "source"));
            var id = Require(ctx, "id");
            using var cts = LinkTimeout(ctx, ct);

            var result = await source.FetchAsync(Http, id, cts.Token).ConfigureAwait(false);
            Vice.Output.Line($"[{result.Id}] {result.Title}");
            foreach (var line in result.MetadataLines)
            {
                Vice.Output.Line(line);
            }

            if (result.Preview.Length > 0)
            {
                Vice.Output.Line();
                Vice.Output.Line(result.Preview);
            }

            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return CommandErrorHandler.Handle(ctx, new TimedOut(ex));
        }
        catch (ViceError error)
        {
            return CommandErrorHandler.Handle(ctx, error);
        }
        catch (HttpRequestException ex)
        {
            return CommandErrorHandler.Handle(ctx, new HttpFailure(ex));
        }
    }

    private static async Task<int> DownloadAsync(CommandContext ctx,
                                                 CancellationToken ct)
    {
        try
        {
            var source = Sources.Resolve(Require(ctx, "source"));
            var id = Require(ctx, "id");
            var format = ResearchOptions.GetFormat(ctx);
            var timeout = ResearchOptions.GetTimeout(ctx);
            using var cts = LinkTimeout(ctx, ct);

            var target = await source.ResolveDownloadAsync(Http, id, format, cts.Token).ConfigureAwait(false);
            var destination = ResearchDownloader.BuildDestinationPath(ctx["path"], source.Name, id, target.Extension);

            if (TrySubmitJob(ctx, source.Name, id, destination, target.Extension, timeout, ct, out var jobTask))
            {
                Vice.Output.Line($"Queued download {source.Name}/{id} -> {destination}.");
                return await jobTask.ConfigureAwait(false);
            }

            var reporter = ctx.Quiet ? null : ProgressLine($"{source.Name}/{id}");
            await ResearchDownloader.DownloadToFileAsync(Http, target.Uri, destination, reporter, cts.Token).ConfigureAwait(false);
            Vice.Output.Line($"Saved {source.Name}/{id} -> {destination}.");
            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return CommandErrorHandler.Handle(ctx, new TimedOut(ex));
        }
        catch (ViceError error)
        {
            return CommandErrorHandler.Handle(ctx, error);
        }
        catch (HttpRequestException ex)
        {
            return CommandErrorHandler.Handle(ctx, new HttpFailure(ex));
        }
    }

    private static async Task<int> ArchiveAsync(CommandContext ctx,
                                                CancellationToken ct)
    {
        try
        {
            var source = Sources.Resolve(Require(ctx, "source"));
            if (!source.Searchable)
            {
                throw new BadArgument($"Source '{source.Name}' is not searchable and cannot be archived; use 'fetch' or 'download' with an id.");
            }

            var query = Require(ctx, "query");
            var format = ResearchOptions.GetFormat(ctx);
            var timeout = ResearchOptions.GetTimeout(ctx);
            var directory = ctx["path"] ?? Path.Combine(Environment.CurrentDirectory, $"{source.Name}-archive");
            using var cts = LinkTimeout(ctx, ct);

            var hits = await RunSearchAsync(ctx, source, query, cts.Token).ConfigureAwait(false);
            if (hits.Count == 0)
            {
                Vice.Output.Line($"No results for '{query}' on {source.Name}.");
                return ViceExitCode.SUCCESS;
            }

            var failures = 0;
            foreach (var hit in hits)
            {
                cts.Token.ThrowIfCancellationRequested();
                try
                {
                    var target = await source.ResolveDownloadAsync(Http, hit.Id, format, cts.Token).ConfigureAwait(false);
                    var destination = ResearchDownloader.BuildDestinationPath(directory, source.Name, hit.Id, target.Extension);

                    if (TrySubmitJob(ctx, source.Name, hit.Id, destination, target.Extension, timeout, ct, out var jobTask))
                    {
                        Vice.Output.Line($"Queued {source.Name}/{hit.Id} -> {destination}.");
                        var exitCode = await jobTask.ConfigureAwait(false);
                        if (exitCode != ViceExitCode.SUCCESS)
                        {
                            failures++;
                            Vice.Output.Error($"Failed {source.Name}/{hit.Id}: queued download exited with code {exitCode}.");
                        }

                        continue;
                    }

                    var reporter = ctx.Quiet ? null : ProgressLine($"{source.Name}/{hit.Id}");
                    await ResearchDownloader.DownloadToFileAsync(Http, target.Uri, destination, reporter, cts.Token).ConfigureAwait(false);
                    Vice.Output.Line($"Saved {source.Name}/{hit.Id} -> {destination}.");
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    failures++;
                    Vice.Output.Error($"Failed {source.Name}/{hit.Id}: request timed out.");
                }
                catch (HttpRequestException ex)
                {
                    failures++;
                    Vice.Output.Error($"Failed {source.Name}/{hit.Id}: {ex.Message}");
                }
                catch (IOException ex)
                {
                    failures++;
                    Vice.Output.Error($"Failed {source.Name}/{hit.Id}: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    failures++;
                    Vice.Output.Error($"Failed {source.Name}/{hit.Id}: {ex.Message}");
                }
            }

            return failures == 0 ? ViceExitCode.SUCCESS : ViceExitCode.FAILURE;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return CommandErrorHandler.Handle(ctx, new TimedOut(ex));
        }
        catch (ViceError error)
        {
            return CommandErrorHandler.Handle(ctx, error);
        }
        catch (HttpRequestException ex)
        {
            return CommandErrorHandler.Handle(ctx, new HttpFailure(ex));
        }
    }

    private static async Task<int> RawDownloadAsync(CommandContext ctx,
                                                    CancellationToken ct)
    {
        try
        {
            var id = Require(ctx, "id");
            if (!TryParseHttpUrl(id, out var uri))
            {
                throw new BadArgument($"'{id}' is not an http(s) URL; use 'download {id} from source <name>' for a source-resolved id.");
            }

            using var cts = LinkTimeout(ctx, ct);

            var fileName = RawDownloadFileName(uri);
            var destination = ResearchDownloader.BuildUrlDestinationPath(ctx["path"], fileName);

            var reporter = ctx.Quiet ? null : ProgressLine(uri.Host);
            await ResearchDownloader.DownloadToFileAsync(Http, uri, destination, reporter, cts.Token).ConfigureAwait(false);
            Vice.Output.Line($"Saved {uri} -> {destination}.");
            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return CommandErrorHandler.Handle(ctx, new TimedOut(ex));
        }
        catch (ViceError error)
        {
            return CommandErrorHandler.Handle(ctx, error);
        }
        catch (HttpRequestException ex)
        {
            return CommandErrorHandler.Handle(ctx, new HttpFailure(ex));
        }
    }

    private static bool TryParseHttpUrl(string value,
                                        out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }

        uri = default!;
        return false;
    }

    private static string RawDownloadFileName(Uri uri)
    {
        var name = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "download.bin";
        }

        return name;
    }

    private static async Task<IReadOnlyList<SearchHit>> RunSearchAsync(ICommandContext ctx,
                                                                       IResearchSource source,
                                                                       string query,
                                                                       CancellationToken ct)
    {
        if (!source.Searchable)
        {
            throw new BadArgument($"Source '{source.Name}' is not searchable; use 'fetch' or 'download' with an id.");
        }

        var paging = ResearchOptions.GetPaging(ctx);
        var timeout = ResearchOptions.GetTimeout(ctx);
        var inflightKey = $"{source.Name}|{query}|{paging.Limit}|{paging.Offset}";
        var lazy = InFlightSearches.GetOrAdd(inflightKey, k => new Lazy<Task<IReadOnlyList<SearchHit>>>(
            () => CoalescedFetchAsync(source, query, paging, timeout, k),
            LazyThreadSafetyMode.ExecutionAndPublication));

        return await lazy.Value.WaitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SearchHit>> CoalescedFetchAsync(IResearchSource source,
                                                                            string query,
                                                                            ResearchPaging paging,
                                                                            TimeSpan timeout,
                                                                            string inflightKey)
    {
        try
        {
            return await FetchSearchAsync(source, query, paging, timeout, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (InFlightSearches.TryGetValue(inflightKey, out var stored))
            {
                InFlightSearches.TryRemove(new KeyValuePair<string, Lazy<Task<IReadOnlyList<SearchHit>>>>(inflightKey, stored));
            }
        }
    }

    private static async Task<IReadOnlyList<SearchHit>> FetchSearchAsync(IResearchSource source,
                                                                         string query,
                                                                         ResearchPaging paging,
                                                                         TimeSpan timeout,
                                                                         CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await source.SearchAsync(Http, query, paging.Limit, paging.Offset, cts.Token).ConfigureAwait(false);
    }

    private static (IResearchSource source, string query) ResolveSearch(ICommandContext ctx)
    {
        var source = Sources.Resolve(Require(ctx, "source"));
        var query = Require(ctx, "query");
        return (source, query);
    }

    public const string TimeoutOptionKey = "timeout-ms";

    private static bool TrySubmitJob(CommandContext ctx,
                                     string source,
                                     string id,
                                     string destination,
                                     string extension,
                                     TimeSpan timeout,
                                     CancellationToken ct,
                                     out Task<int> jobTask)
    {
        var session = ctx.Session;
        if (session is null || !session.IsInteractive)
        {
            jobTask = Task.FromResult(ViceExitCode.SUCCESS);
            return false;
        }

        var options = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [TimeoutOptionKey] = ((long)timeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        var descriptor = JobDescriptor.ForDownload(source, id, destination, extension, options);
        jobTask = session.Jobs.SubmitAsync(descriptor, ct);
        return true;
    }

    private static CancellationTokenSource LinkTimeout(ICommandContext ctx,
                                                       CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ResearchOptions.GetTimeout(ctx));
        return cts;
    }

    private static IProgress<DownloadProgress> ProgressLine(string label)
    {
        return new Progress<DownloadProgress>(p =>
        {
            var pct = p.Percentage is { } v ? $"{v:0.0}%" : p.FormatSize();
            Vice.Output.Write($"\r{label}: {pct}      ");
        });
    }

    private static string Require(ICommandContext ctx,
                                  string name)
    {
        return ctx[name] ?? throw new BadArgument($"Required target '{name}' was not provided.");
    }

    private static string Truncate(string value,
                                   int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        return $"{value[..(max - 1)]}…";
    }
}
