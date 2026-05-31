namespace Vice.Net.Research;

public sealed record SearchHit(string Id,
                               string Title,
                               string Summary);

public sealed record FetchResult(string Id,
                                 string Title,
                                 IReadOnlyList<string> MetadataLines,
                                 string Preview);

public sealed record DownloadTarget(Uri Uri,
                                    string Extension);

public interface IResearchSource
{
    string Name { get; }

    IReadOnlyList<string> Aliases { get; }

    bool Searchable { get; }

    string DefaultExtension { get; }

    Task<IReadOnlyList<SearchHit>> SearchAsync(HttpClient http,
                                               string query,
                                               int limit,
                                               int offset,
                                               CancellationToken ct);

    Task<FetchResult> FetchAsync(HttpClient http,
                                 string id,
                                 CancellationToken ct);

    Task<DownloadTarget> ResolveDownloadAsync(HttpClient http,
                                              string id,
                                              string? format,
                                              CancellationToken ct);
}
