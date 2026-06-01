using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Vice.Logging;

namespace Vice.Net.Research;

internal sealed class PubMedSource : IResearchSource
{
    private const string Base = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils";

    private const string TOOL_NAME = "vice";

    private const string API_KEY_ENV_VAR = "VICE_NCBI_API_KEY";

    public string Name => "pubmed";

    public IReadOnlyList<string> Aliases => new[] { "pmc" };

    public bool Searchable => true;

    public string DefaultExtension => "xml";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(HttpClient http,
                                                            string query,
                                                            int limit,
                                                            int offset,
                                                            CancellationToken ct)
    {
        var searchUrl = $"{Base}/esearch.fcgi?db=pubmed&term={WebUtility.UrlEncode(query)}&retstart={offset}&retmax={limit}&retmode=json{Identification()}";
        var searchJson = await http.GetStringAsync(searchUrl, ct).ConfigureAwait(false);

        var ids = ExtractIds(searchJson);
        if (ids.Count == 0)
        {
            return Array.Empty<SearchHit>();
        }

        var summaryUrl = $"{Base}/esummary.fcgi?db=pubmed&id={string.Join(",", ids)}&retmode=json{Identification()}";
        var summaryJson = await http.GetStringAsync(summaryUrl, ct).ConfigureAwait(false);

        return BuildHits(ids, summaryJson);
    }

    public async Task<FetchResult> FetchAsync(HttpClient http,
                                              string id,
                                              CancellationToken ct)
    {
        var url = $"{Base}/efetch.fcgi?db=pubmed&id={WebUtility.UrlEncode(id)}&rettype=abstract&retmode=xml{Identification()}";
        var xml = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);

        var article = doc.Descendants("PubmedArticle").FirstOrDefault()
            ?? throw new BadArgument($"PubMed returned no article for PMID '{id}'.");

        var title = Collapse(article.Descendants("ArticleTitle").FirstOrDefault()?.Value);
        var abstractText = string.Join("\n",
            article.Descendants("AbstractText").Select(a => Collapse(a.Value)).Where(t => t.Length > 0));

        var authors = article.Descendants("Author")
            .Select(a => $"{Collapse(a.Element("ForeName")?.Value)} {Collapse(a.Element("LastName")?.Value)}".Trim())
            .Where(n => n.Length > 0)
            .ToArray();

        var journal = Collapse(article.Descendants("Journal").FirstOrDefault()?.Element("Title")?.Value);
        var pubDate = article.Descendants("PubDate").FirstOrDefault();
        var date = pubDate is null
            ? string.Empty
            : Collapse(string.Join(" ", pubDate.Elements().Select(e => e.Value)));

        var meshHeadings = article.Descendants("MeshHeading")
            .Select(m => Collapse(m.Element("DescriptorName")?.Value))
            .Where(m => m.Length > 0)
            .ToArray();

        var metadata = new List<string>
        {
            $"Authors: {string.Join(", ", authors)}",
            $"Journal: {journal}",
            $"Date: {date}",
            $"MeSH: {string.Join("; ", meshHeadings)}",
        };

        return new FetchResult(id, title, metadata, abstractText);
    }

    public Task<DownloadTarget> ResolveDownloadAsync(HttpClient http,
                                                     string id,
                                                     string? format,
                                                     CancellationToken ct)
    {
        var uri = new Uri($"{Base}/efetch.fcgi?db=pubmed&id={WebUtility.UrlEncode(id)}&rettype=abstract&retmode=xml{Identification()}");
        Vice.Log.Emit(ViceLogLevel.Debug,
                      $"research source {Name} resolved {id} to {uri}");
        return Task.FromResult(new DownloadTarget(uri, DefaultExtension));
    }

    private static string Identification()
    {
        var parts = new List<string>
        {
            $"tool={WebUtility.UrlEncode(TOOL_NAME)}",
        };

        var contact = Environment.GetEnvironmentVariable(ResearchHttp.ContactEmailEnvVar);
        if (!string.IsNullOrWhiteSpace(contact))
        {
            parts.Add($"email={WebUtility.UrlEncode(contact.Trim())}");
        }

        var apiKey = Environment.GetEnvironmentVariable(API_KEY_ENV_VAR);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            parts.Add($"api_key={WebUtility.UrlEncode(apiKey.Trim())}");
        }

        return $"&{string.Join("&", parts)}";
    }

    private static IReadOnlyList<string> ExtractIds(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("esearchresult", out var result)
            || !result.TryGetProperty("idlist", out var idlist)
            || idlist.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return idlist.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .ToArray();
    }

    private static IReadOnlyList<SearchHit> BuildHits(IReadOnlyList<string> ids,
                                                      string summaryJson)
    {
        using var doc = JsonDocument.Parse(summaryJson);
        if (!doc.RootElement.TryGetProperty("result", out var result))
        {
            return ids.Select(id => new SearchHit(id, string.Empty, string.Empty)).ToArray();
        }

        var hits = new List<SearchHit>(ids.Count);
        foreach (var id in ids)
        {
            if (!result.TryGetProperty(id, out var entry))
            {
                hits.Add(new SearchHit(id, string.Empty, string.Empty));
                continue;
            }

            var title = entry.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty;
            var source = entry.TryGetProperty("source", out var srcEl) ? srcEl.GetString() ?? string.Empty : string.Empty;
            var date = entry.TryGetProperty("pubdate", out var dateEl) ? dateEl.GetString() ?? string.Empty : string.Empty;
            var summary = string.Join(" ", new[] { source, date }.Where(s => s.Length > 0));
            hits.Add(new SearchHit(id, title, summary));
        }

        return hits;
    }

    private static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
