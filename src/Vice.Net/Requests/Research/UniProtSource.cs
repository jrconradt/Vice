using System.Net;
using System.Text.Json;
using Vice.Logging;

namespace Vice.Net.Research;

internal sealed class UniProtSource : IResearchSource
{
    private const string DEFAULT_BASE = "https://rest.uniprot.org/uniprotkb";
    private const string BASE_URL_ENV_VAR = "VICE_UNIPROT_BASE_URL";
    private const int MaxFetch = 500;

    private static readonly string Base = ResolveBase();

    public string Name => "uniprot";

    public IReadOnlyList<string> Aliases => Array.Empty<string>();

    public bool Searchable => true;

    public string DefaultExtension => "fasta";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(HttpClient http,
                                                            string query,
                                                            int limit,
                                                            int offset,
                                                            CancellationToken ct)
    {
        var size = Math.Min(offset + limit, MaxFetch);
        var url = $"{Base}/search?query={WebUtility.UrlEncode(query)}&format=json&size={size}&fields=accession,protein_name,organism_name";
        var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SearchHit>();
        }

        var hits = new List<SearchHit>();
        var index = 0;
        foreach (var entry in results.EnumerateArray())
        {
            if (index++ < offset)
            {
                continue;
            }

            if (hits.Count >= limit)
            {
                break;
            }

            var accession = entry.TryGetProperty("primaryAccession", out var accEl) ? accEl.GetString() ?? string.Empty : string.Empty;
            var name = ProteinName(entry);
            var organism = entry.TryGetProperty("organism", out var orgEl) && orgEl.TryGetProperty("scientificName", out var sciEl)
                ? sciEl.GetString() ?? string.Empty
                : string.Empty;
            hits.Add(new SearchHit(accession, name, organism));
        }

        return hits;
    }

    public async Task<FetchResult> FetchAsync(HttpClient http,
                                              string id,
                                              CancellationToken ct)
    {
        var jsonUrl = $"{Base}/{WebUtility.UrlEncode(id)}.json";
        var json = await http.GetStringAsync(jsonUrl, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var name = ProteinName(root);
        var organism = root.TryGetProperty("organism", out var orgEl) && orgEl.TryGetProperty("scientificName", out var sciEl)
            ? sciEl.GetString() ?? string.Empty
            : string.Empty;

        var function = FunctionAnnotation(root);

        var fastaUrl = $"{Base}/{WebUtility.UrlEncode(id)}.fasta";
        var fasta = await http.GetStringAsync(fastaUrl, ct).ConfigureAwait(false);

        var metadata = new List<string>
        {
            $"Organism: {organism}",
            $"Function: {function}",
        };

        return new FetchResult(id, name, metadata, fasta);
    }

    public Task<DownloadTarget> ResolveDownloadAsync(HttpClient http,
                                                     string id,
                                                     string? format,
                                                     CancellationToken ct)
    {
        var extension = format switch
        {
            "json" => "json",
            "xml" => "xml",
            "gff" => "gff",
            _ => DefaultExtension,
        };

        var uri = new Uri($"{Base}/{WebUtility.UrlEncode(id)}.{extension}");
        Vice.Log.Emit(ViceLogLevel.Debug,
                      $"research source {Name} resolved {id} to {uri}");
        return Task.FromResult(new DownloadTarget(uri, extension));
    }

    private static string ResolveBase()
    {
        var configured = Environment.GetEnvironmentVariable(BASE_URL_ENV_VAR);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().TrimEnd('/');
        }

        return DEFAULT_BASE;
    }

    private static string ProteinName(JsonElement entry)
    {
        if (entry.TryGetProperty("proteinDescription", out var desc)
            && desc.TryGetProperty("recommendedName", out var rec)
            && rec.TryGetProperty("fullName", out var full)
            && full.TryGetProperty("value", out var value))
        {
            return value.GetString() ?? string.Empty;
        }

        if (entry.TryGetProperty("proteinName", out var pn))
        {
            return pn.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string FunctionAnnotation(JsonElement root)
    {
        if (!root.TryGetProperty("comments", out var comments) || comments.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var comment in comments.EnumerateArray())
        {
            if (!comment.TryGetProperty("commentType", out var typeEl)
                || !string.Equals(typeEl.GetString(), "FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (comment.TryGetProperty("texts", out var texts) && texts.ValueKind == JsonValueKind.Array)
            {
                var parts = texts.EnumerateArray()
                    .Select(t => t.TryGetProperty("value", out var v) ? v.GetString() ?? string.Empty : string.Empty)
                    .Where(s => s.Length > 0);
                return string.Join(" ", parts);
            }
        }

        return string.Empty;
    }
}
