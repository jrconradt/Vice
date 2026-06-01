using Vice.Net.Research;
using Xunit;

namespace Vice.Net.Tests;

[Trait("Category", "Live")]
public sealed class ResearchSourcesLiveTests
{
    [LiveNetFact]
    public async Task Arxiv_Search_RealEndpoint_ExtractsHits()
    {
        using var http = ResearchHttp.Create();
        var arxiv = new ArxivSource();

        var hits = await arxiv.SearchAsync(http,
                                           "electron",
                                           3,
                                           0,
                                           CancellationToken.None);

        Assert.NotEmpty(hits);
        Assert.All(hits, hit => Assert.False(string.IsNullOrWhiteSpace(hit.Id)));
        Assert.All(hits, hit => Assert.False(string.IsNullOrWhiteSpace(hit.Title)));
    }

    [LiveNetFact]
    public async Task Arxiv_Fetch_RealEndpoint_BuildsMetadata()
    {
        using var http = ResearchHttp.Create();
        var arxiv = new ArxivSource();

        var result = await arxiv.FetchAsync(http,
                                            "1706.03762",
                                            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.Contains(result.MetadataLines, line => line.StartsWith("Authors:", StringComparison.Ordinal));
    }

    [LiveNetFact]
    public async Task PubMed_Search_RealEndpoint_ExtractsHits()
    {
        using var http = ResearchHttp.Create();
        var pubmed = new PubMedSource();

        var hits = await pubmed.SearchAsync(http,
                                            "crispr",
                                            3,
                                            0,
                                            CancellationToken.None);

        Assert.NotEmpty(hits);
        Assert.All(hits, hit => Assert.False(string.IsNullOrWhiteSpace(hit.Id)));
    }

    [LiveNetFact]
    public async Task Gutenberg_Search_RealEndpoint_ExtractsHits()
    {
        using var http = ResearchHttp.Create();
        var gutenberg = new GutenbergSource();

        var hits = await gutenberg.SearchAsync(http,
                                               "frankenstein",
                                               3,
                                               0,
                                               CancellationToken.None);

        Assert.NotEmpty(hits);
        Assert.All(hits, hit => Assert.False(string.IsNullOrWhiteSpace(hit.Id)));
        Assert.All(hits, hit => Assert.False(string.IsNullOrWhiteSpace(hit.Title)));
    }

    [LiveNetFact]
    public async Task Gutenberg_ResolveDownload_RealEndpoint_SelectsPlainText()
    {
        using var http = ResearchHttp.Create();
        var gutenberg = new GutenbergSource();

        var target = await gutenberg.ResolveDownloadAsync(http,
                                                          "84",
                                                          null,
                                                          CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(target.Extension));
        Assert.True(target.Uri.IsAbsoluteUri);
    }

    [LiveNetFact]
    public async Task UniProt_Search_RealEndpoint_ExtractsName()
    {
        using var http = ResearchHttp.Create();
        var uniprot = new UniProtSource();

        var hits = await uniprot.SearchAsync(http,
                                             "insulin",
                                             3,
                                             0,
                                             CancellationToken.None);

        Assert.NotEmpty(hits);
        Assert.All(hits, hit => Assert.False(string.IsNullOrWhiteSpace(hit.Id)));
        Assert.All(hits, hit => Assert.False(string.IsNullOrWhiteSpace(hit.Title)));
    }

    [LiveNetFact]
    public async Task AlphaFold_Fetch_RealEndpoint_BuildsMetadata()
    {
        using var http = ResearchHttp.Create();
        var alphafold = new AlphaFoldSource();

        var result = await alphafold.FetchAsync(http,
                                                "P12345",
                                                CancellationToken.None);

        Assert.Contains(result.MetadataLines, line => line.StartsWith("PDB:", StringComparison.Ordinal));
        Assert.Contains(result.MetadataLines, line => line.StartsWith("mmCIF:", StringComparison.Ordinal));
    }
}
