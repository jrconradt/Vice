using System.Net;
using System.Xml.Linq;
using Vice.Logging;

namespace Vice.Net.Research;

internal sealed class ArxivSource : IResearchSource
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace ArxivNs = "http://arxiv.org/schemas/atom";

    public string Name => "arxiv";

    public IReadOnlyList<string> Aliases => Array.Empty<string>();

    public bool Searchable => true;

    public string DefaultExtension => "pdf";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(HttpClient http,
                                                            string query,
                                                            int limit,
                                                            int offset,
                                                            CancellationToken ct)
    {
        var url = $"https://export.arxiv.org/api/query?search_query=all:{WebUtility.UrlEncode(query)}&start={offset}&max_results={limit}";
        var xml = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);

        var hits = new List<SearchHit>();
        foreach (var entry in doc.Descendants(Atom + "entry"))
        {
            var id = ExtractId(entry);
            var title = Collapse(entry.Element(Atom + "title")?.Value);
            var summary = Collapse(entry.Element(Atom + "summary")?.Value);
            hits.Add(new SearchHit(id, title, summary));
        }

        return hits;
    }

    public async Task<FetchResult> FetchAsync(HttpClient http,
                                              string id,
                                              CancellationToken ct)
    {
        var url = $"https://export.arxiv.org/api/query?id_list={WebUtility.UrlEncode(id)}";
        var xml = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);
        var entry = doc.Descendants(Atom + "entry").FirstOrDefault()
            ?? throw new BadArgument($"arXiv returned no entry for id '{id}'.");

        var title = Collapse(entry.Element(Atom + "title")?.Value);
        var abstractText = Collapse(entry.Element(Atom + "summary")?.Value);

        var authors = entry.Elements(Atom + "author")
            .Select(a => Collapse(a.Element(Atom + "name")?.Value))
            .Where(n => n.Length > 0)
            .ToArray();

        var categories = entry.Elements(Atom + "category")
            .Select(c => c.Attribute("term")?.Value ?? string.Empty)
            .Where(t => t.Length > 0)
            .ToArray();

        var comment = Collapse(entry.Element(ArxivNs + "comment")?.Value);

        var metadata = new List<string>
        {
            $"Authors: {string.Join(", ", authors)}",
            $"Categories: {string.Join(", ", categories)}",
        };
        if (comment.Length > 0)
        {
            metadata.Add($"Comment: {comment}");
        }

        return new FetchResult(ExtractId(entry), title, metadata, abstractText);
    }

    public Task<DownloadTarget> ResolveDownloadAsync(HttpClient http,
                                                     string id,
                                                     string? format,
                                                     CancellationToken ct)
    {
        var uri = new Uri($"https://export.arxiv.org/pdf/{WebUtility.UrlEncode(id)}");
        Vice.Log.Emit(ViceLogLevel.Debug,
                      $"research source {Name} resolved {id} to {uri}");
        return Task.FromResult(new DownloadTarget(uri, DefaultExtension));
    }

    private static string ExtractId(XElement entry)
    {
        var raw = entry.Element(Atom + "id")?.Value ?? string.Empty;
        var slash = raw.LastIndexOf("/abs/", StringComparison.Ordinal);
        if (slash >= 0)
        {
            return raw[(slash + 5)..];
        }

        var lastSlash = raw.LastIndexOf('/');
        return lastSlash >= 0 ? raw[(lastSlash + 1)..] : raw;
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
