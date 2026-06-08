using System.Net;
using System.Text.Json;
using Vice.Logging;

namespace Vice.Research;

internal sealed class GutenbergSource : IResearchSource
{
    public string Name => "gutenberg";

    public IReadOnlyList<string> Aliases => new[] { "gutenberg.org" };

    public bool Searchable => true;

    public string DefaultExtension => "txt";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(HttpClient http,
                                                            string query,
                                                            int limit,
                                                            int offset,
                                                            CancellationToken ct)
    {
        var hits = new List<SearchHit>();
        if (limit <= 0)
        {
            return hits;
        }

        var index = 0;

        for (var page = 1; ; page++)
        {
            if (hits.Count >= limit)
            {
                break;
            }

            var url = $"https://gutendex.com/books?search={WebUtility.UrlEncode(query)}&page={page}";
            var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var pageHadResults = false;
            foreach (var book in results.EnumerateArray())
            {
                pageHadResults = true;
                var absolute = index++;
                if (absolute < offset)
                {
                    continue;
                }

                if (hits.Count >= limit)
                {
                    break;
                }

                var id = book.TryGetProperty("id", out var idEl) ? idEl.GetRawText() : string.Empty;
                var title = book.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty;
                var authors = AuthorNames(book);
                hits.Add(new SearchHit(id, title, authors));
            }

            if (!pageHadResults)
            {
                break;
            }
        }

        return hits;
    }

    public async Task<FetchResult> FetchAsync(HttpClient http,
                                              string id,
                                              CancellationToken ct)
    {
        var meta = await GetBookAsync(http, id, ct).ConfigureAwait(false);
        var root = meta.RootElement;

        var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty;
        var authors = AuthorNames(root);

        var languages = root.TryGetProperty("languages", out var langEl) && langEl.ValueKind == JsonValueKind.Array
            ? string.Join(", ", langEl.EnumerateArray().Select(l => l.GetString()).Where(l => l is not null))
            : string.Empty;

        var downloadCount = root.TryGetProperty("download_count", out var dcEl) ? dcEl.GetRawText() : "0";

        var metadata = new List<string>
        {
            $"Authors: {authors}",
            $"Languages: {languages}",
            $"Downloads: {downloadCount}",
        };

        var textUrl = SelectFormat(root, null);
        var preview = string.Empty;
        if (textUrl is not null)
        {
            var body = await http.GetStringAsync(textUrl, ct).ConfigureAwait(false);
            preview = body.Length > 2000 ? body[..2000] : body;
        }

        return new FetchResult(id, title, metadata, preview);
    }

    public async Task<DownloadTarget> ResolveDownloadAsync(HttpClient http,
                                                           string id,
                                                           string? format,
                                                           CancellationToken ct,
                                                           IViceLogger logger)
    {
        var extension = format switch
        {
            "epub" => "epub",
            "html" => "html",
            _ => DefaultExtension,
        };

        using var meta = await GetBookAsync(http, id, ct).ConfigureAwait(false);
        var url = SelectFormat(meta.RootElement, format)
            ?? throw new BadArgument($"Gutenberg book {id} has no downloadable '{format ?? "text"}' format.");
        logger.Log(ViceLogLevel.Debug,
                      $"research source {Name} resolved {id} to {url}");
        return new DownloadTarget(new Uri(url), extension);
    }

    private async Task<JsonDocument> GetBookAsync(HttpClient http,
                                                  string id,
                                                  CancellationToken ct)
    {
        if (!int.TryParse(id, out _))
        {
            throw new BadArgument($"Gutenberg id '{id}' must be a numeric book number.");
        }

        var url = $"https://gutendex.com/books/{id}";
        var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        return JsonDocument.Parse(json);
    }

    private static string AuthorNames(JsonElement book)
    {
        if (!book.TryGetProperty("authors", out var authors) || authors.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var names = authors.EnumerateArray()
            .Select(a => a.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty)
            .Where(n => n.Length > 0);
        return string.Join("; ", names);
    }

    private static string? SelectFormat(JsonElement book,
                                        string? format)
    {
        if (!book.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var wanted = format switch
        {
            "epub" => "application/epub+zip",
            "html" => "text/html",
            _ => "text/plain",
        };

        string? fallback = null;
        foreach (var entry in formats.EnumerateObject())
        {
            var mime = entry.Name;
            var value = entry.Value.GetString();
            if (value is null || value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (mime.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            if (format is null && mime.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                fallback ??= value;
            }
        }

        return fallback;
    }
}
