using System.Net;
using System.Text.Json;
using Vice.Logging;

namespace Vice.Net.Research;

internal sealed class AlphaFoldSource : IResearchSource
{
    private const string Api = "https://alphafold.ebi.ac.uk/api/prediction";

    public string Name => "alphafold";

    public IReadOnlyList<string> Aliases => new[] { "af" };

    public bool Searchable => false;

    public string DefaultExtension => "pdb";

    public Task<IReadOnlyList<SearchHit>> SearchAsync(HttpClient http,
                                                      string query,
                                                      int limit,
                                                      int offset,
                                                      CancellationToken ct)
    {
        throw new BadArgument("AlphaFold is not searchable; use 'fetch' or 'download' with a UniProt accession.");
    }

    public async Task<FetchResult> FetchAsync(HttpClient http,
                                              string id,
                                              CancellationToken ct)
    {
        var entry = await GetPredictionAsync(http, id, ct).ConfigureAwait(false);

        var gene = entry.TryGetProperty("gene", out var geneEl) ? geneEl.GetString() ?? string.Empty : string.Empty;
        var organism = entry.TryGetProperty("organismScientificName", out var orgEl) ? orgEl.GetString() ?? string.Empty : string.Empty;
        var description = entry.TryGetProperty("uniprotDescription", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty;
        var modelDate = entry.TryGetProperty("modelCreatedDate", out var dateEl) ? dateEl.GetString() ?? string.Empty : string.Empty;

        var metadata = new List<string>
        {
            $"Gene: {gene}",
            $"Organism: {organism}",
            $"Model created: {modelDate}",
        };

        AddUrl(metadata, entry, "pdbUrl", "PDB");
        AddUrl(metadata, entry, "cifUrl", "mmCIF");
        AddUrl(metadata, entry, "bcifUrl", "bCIF");
        AddUrl(metadata, entry, "paeImageUrl", "PAE image");
        AddUrl(metadata, entry, "paeDocUrl", "PAE data");

        return new FetchResult(id, description, metadata, string.Empty);
    }

    public async Task<DownloadTarget> ResolveDownloadAsync(HttpClient http,
                                                           string id,
                                                           string? format,
                                                           CancellationToken ct)
    {
        var entry = await GetPredictionAsync(http, id, ct).ConfigureAwait(false);

        var (key, extension) = format switch
        {
            "cif" or "mmcif" => ("cifUrl", "cif"),
            "bcif" => ("bcifUrl", "bcif"),
            "pae" => ("paeDocUrl", "json"),
            _ => ("pdbUrl", DefaultExtension),
        };

        if (!entry.TryGetProperty(key, out var urlEl) || urlEl.GetString() is not { Length: > 0 } url)
        {
            throw new BadArgument($"AlphaFold prediction for {id} has no '{format ?? "pdb"}' structure URL.");
        }

        Vice.Log.Emit(ViceLogLevel.Debug,
                      $"research source {Name} resolved {id} to {url}");
        return new DownloadTarget(new Uri(url), extension);
    }

    private async Task<JsonElement> GetPredictionAsync(HttpClient http,
                                                       string id,
                                                       CancellationToken ct)
    {
        var url = $"{Api}/{WebUtility.UrlEncode(id)}";
        var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            throw new BadArgument($"AlphaFold returned no prediction for accession '{id}'.");
        }

        return doc.RootElement[0].Clone();
    }

    private static void AddUrl(List<string> metadata,
                               JsonElement entry,
                               string property,
                               string label)
    {
        if (entry.TryGetProperty(property, out var el) && el.GetString() is { Length: > 0 } url)
        {
            metadata.Add($"{label}: {url}");
        }
    }
}
