using Vice.Composition;
using Vice.Contracts;
using Vice.Core;
using Vice.Execution;
using Vice.Foundation.Execution;
using Vice.Lexicon;
using Vice.Logging;
using Vice.Net.Requests.Http;

namespace Vice.Research;

[ViceCommandPack]
public static class ResearchCommands
{
    private static HttpClient HttpFor(ICommandContext ctx)
    {
        var service = ctx.Session?.GetService<ResearchHttpService>()
            ?? throw new InvalidOperationException(
                $"Research commands require a {nameof(ResearchHttpService)} session service; register one on the host.");
        return service.Client;
    }

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

    private static async Task<int> RunGuarded(CommandContext ctx,
                                              CancellationToken ct,
                                              Func<Task<int>> body)
    {
        try
        {
            return await body().ConfigureAwait(false);
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

    private static async Task<int> RunStreamingGuarded(IStreamingCommandContext<byte[]> ctx,
                                                       CancellationToken ct,
                                                       Func<Task<int>> body)
    {
        try
        {
            return await body().ConfigureAwait(false);
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

    private static Task<int> SearchAsStream(IStreamingCommandContext<byte[]> ctx,
                                            CancellationToken ct)
    {
        return RunStreamingGuarded(ctx, ct, async () =>
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
        });
    }

    private static Task<int> SearchToConsole(CommandContext ctx,
                                             CancellationToken ct)
    {
        return RunGuarded(ctx, ct, () =>
        {
            var (source, query) = ResolveSearch(ctx);
            return SearchToConsoleBody(ctx, source, query, ct);
        });
    }

    private static async Task<int> SearchToConsoleBody(CommandContext ctx,
                                                       IResearchSource source,
                                                       string query,
                                                       CancellationToken ct)
    {
        var hits = await RunSearchAsync(ctx, source, query, ct).ConfigureAwait(false);
        if (hits.Count == 0)
        {
            ctx.Console.WriteLine($"No results for '{query}' on {source.Name}.");
            return ViceExitCode.SUCCESS;
        }

        foreach (var hit in hits)
        {
            ctx.Console.WriteLine($"{hit.Id,-16}  {Truncate(hit.Title, 70)}");
            if (hit.Summary.Length > 0)
            {
                ctx.Console.WriteLine($"                  {Truncate(hit.Summary, 70)}");
            }
        }

        return ViceExitCode.SUCCESS;
    }

    private static Task<int> FetchAsync(CommandContext ctx,
                                        CancellationToken ct)
    {
        return RunGuarded(ctx, ct, async () =>
        {
            var source = ResearchSources.Resolve(Require(ctx, "source"));
            var id = Require(ctx, "id");
            using var cts = LinkTimeout(ctx, ct);

            var result = await source.FetchAsync(HttpFor(ctx), id, cts.Token).ConfigureAwait(false);
            ctx.Console.WriteLine($"[{result.Id}] {result.Title}");
            foreach (var line in result.MetadataLines)
            {
                ctx.Console.WriteLine(line);
            }

            if (result.Preview.Length > 0)
            {
                ctx.Console.WriteLine();
                ctx.Console.WriteLine(result.Preview);
            }

            return ViceExitCode.SUCCESS;
        });
    }

    private static Task<int> DownloadAsync(CommandContext ctx,
                                           CancellationToken ct)
    {
        return RunGuarded(ctx, ct, async () =>
        {
            var source = ResearchSources.Resolve(Require(ctx, "source"));
            var id = Require(ctx, "id");
            var format = ResearchOptions.GetFormat(ctx);
            var timeout = ResearchOptions.GetTimeout(ctx);
            using var cts = LinkTimeout(ctx, ct);

            var http = HttpFor(ctx);
            var target = await source.ResolveDownloadAsync(http, id, format, cts.Token, ctx.Logger).ConfigureAwait(false);
            var destination = ResearchDownloadPaths.BuildDestinationPath(ctx["path"], source.Name, id, target.Extension);

            if (TrySubmitJob(ctx, source.Name, id, destination, target.Extension, format, timeout, ct, out var jobTask))
            {
                var jobId = await jobTask.ConfigureAwait(false);
                ctx.Console.WriteLine($"Queued download {source.Name}/{id} -> {destination} (#{jobId}).");
                return ViceExitCode.SUCCESS;
            }

            var reporter = ctx.Quiet ? null : ProgressLine(ctx, $"{source.Name}/{id}");
            await ResumableHttpDownload.ToFileAsync(http, target.Uri, destination, reporter, ctx.Logger, cts.Token).ConfigureAwait(false);
            ctx.Console.WriteLine($"Saved {source.Name}/{id} -> {destination}.");
            return ViceExitCode.SUCCESS;
        });
    }

    private static Task<int> ArchiveAsync(CommandContext ctx,
                                          CancellationToken ct)
    {
        return RunGuarded(ctx, ct, async () =>
        {
            var source = ResearchSources.Resolve(Require(ctx, "source"));
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
                ctx.Console.WriteLine($"No results for '{query}' on {source.Name}.");
                return ViceExitCode.SUCCESS;
            }

            var failures = 0;
            foreach (var hit in hits)
            {
                cts.Token.ThrowIfCancellationRequested();
                try
                {
                    var target = await source.ResolveDownloadAsync(HttpFor(ctx), hit.Id, format, cts.Token, ctx.Logger).ConfigureAwait(false);
                    var destination = ResearchDownloadPaths.BuildDestinationPath(directory, source.Name, hit.Id, target.Extension);

                    if (TrySubmitJob(ctx, source.Name, hit.Id, destination, target.Extension, format, timeout, ct, out var jobTask))
                    {
                        var jobId = await jobTask.ConfigureAwait(false);
                        ctx.Console.WriteLine($"Queued {source.Name}/{hit.Id} -> {destination} (#{jobId}).");
                        continue;
                    }

                    var reporter = ctx.Quiet ? null : ProgressLine(ctx, $"{source.Name}/{hit.Id}");
                    await ResumableHttpDownload.ToFileAsync(HttpFor(ctx), target.Uri, destination, reporter, ctx.Logger, cts.Token).ConfigureAwait(false);
                    ctx.Console.WriteLine($"Saved {source.Name}/{hit.Id} -> {destination}.");
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    failures++;
                    ctx.Console.WriteError($"Failed {source.Name}/{hit.Id}: request timed out.");
                }
                catch (HttpRequestException ex)
                {
                    failures++;
                    ctx.Console.WriteError($"Failed {source.Name}/{hit.Id}: {ex.Message}");
                }
                catch (IOException ex)
                {
                    failures++;
                    ctx.Console.WriteError($"Failed {source.Name}/{hit.Id}: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    failures++;
                    ctx.Console.WriteError($"Failed {source.Name}/{hit.Id}: {ex.Message}");
                }
            }

            return failures == 0 ? ViceExitCode.SUCCESS : ViceExitCode.FAILURE;
        });
    }

    private static Task<int> RawDownloadAsync(CommandContext ctx,
                                              CancellationToken ct)
    {
        return RunGuarded(ctx, ct, async () =>
        {
            var id = Require(ctx, "id");
            if (!TryParseHttpUrl(id, out var uri))
            {
                throw new BadArgument($"'{id}' is not an http(s) URL; use 'download {id} from source <name>' for a source-resolved id.");
            }

            using var cts = LinkTimeout(ctx, ct);

            var fileName = RawDownloadFileName(uri);
            var destination = ResearchDownloadPaths.BuildUrlDestinationPath(ctx["path"], fileName);

            var reporter = ctx.Quiet ? null : ProgressLine(ctx, uri.Host);
            await ResumableHttpDownload.ToFileAsync(HttpFor(ctx), uri, destination, reporter, ctx.Logger, cts.Token).ConfigureAwait(false);
            ctx.Console.WriteLine($"Saved {uri} -> {destination}.");
            return ViceExitCode.SUCCESS;
        });
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

        var http = HttpFor(ctx);
        var paging = ResearchOptions.GetPaging(ctx);
        var timeout = ResearchOptions.GetTimeout(ctx);
        return await FetchSearchAsync(http, source, query, paging, timeout, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SearchHit>> FetchSearchAsync(HttpClient http,
                                                                         IResearchSource source,
                                                                         string query,
                                                                         ResearchPaging paging,
                                                                         TimeSpan timeout,
                                                                         CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await source.SearchAsync(http, query, paging.Limit, paging.Offset, cts.Token).ConfigureAwait(false);
    }

    private static (IResearchSource source, string query) ResolveSearch(ICommandContext ctx)
    {
        var source = ResearchSources.Resolve(Require(ctx, "source"));
        var query = Require(ctx, "query");
        return (source, query);
    }

    public const string TimeoutOptionKey = "timeout-ms";

    private static bool TrySubmitJob(CommandContext ctx,
                                     string source,
                                     string id,
                                     string destination,
                                     string extension,
                                     string? format,
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

        var descriptor = ResearchDownloadJobRunner.DescriptorFor(source,
                                                                 id,
                                                                 destination,
                                                                 extension,
                                                                 format,
                                                                 options);
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

    private static IProgress<DownloadProgress> ProgressLine(CommandContext ctx, string label)
    {
        return new Progress<DownloadProgress>(p =>
        {
            var pct = p.Percentage is { } v ? $"{v:0.0}%" : p.FormatSize();
            ctx.Console.Write($"\r{label}: {pct}      ");
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
