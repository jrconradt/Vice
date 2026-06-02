using Vice.Logging;
using Vice.Net.Research;
using Xunit;

namespace Vice.Net.Tests;

public sealed class ResearchSourcesTests
{
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        return new HttpClient(new StubHttpMessageHandler(responder));
    }

    private static HttpResponseMessage Xml(string body)
    {
        return StubHttpMessageHandler.Ok(body, "application/xml");
    }

    private static HttpResponseMessage Json(string body)
    {
        return StubHttpMessageHandler.Ok(body, "application/json");
    }

    [Fact]
    public async Task Arxiv_Search_ExtractsAbsIdAndCollapsesWhitespace()
    {
        const string Feed = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <id>http://arxiv.org/abs/2401.00001v2</id>
                <title>  A   spread   title  </title>
                <summary>line one
            line two</summary>
              </entry>
            </feed>
            """;

        var arxiv = new ArxivSource();
        var hits = await arxiv.SearchAsync(Client(_ => Xml(Feed)), "anything", 10, 0, CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("2401.00001v2", hit.Id);
        Assert.Equal("A spread title", hit.Title);
        Assert.Equal("line one line two", hit.Summary);
    }

    [Fact]
    public async Task Arxiv_Search_FallsBackToLastSlashWhenNoAbsSegment()
    {
        const string Feed = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <id>http://example.org/papers/xyz-99</id>
                <title>t</title>
                <summary>s</summary>
              </entry>
            </feed>
            """;

        var arxiv = new ArxivSource();
        var hits = await arxiv.SearchAsync(Client(_ => Xml(Feed)), "q", 5, 0, CancellationToken.None);

        Assert.Equal("xyz-99", Assert.Single(hits).Id);
    }

    [Fact]
    public async Task Arxiv_Search_RawIdWhenNoSlashPresent()
    {
        const string Feed = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <id>bare-identifier</id>
                <title>t</title>
                <summary>s</summary>
              </entry>
            </feed>
            """;

        var arxiv = new ArxivSource();
        var hits = await arxiv.SearchAsync(Client(_ => Xml(Feed)), "q", 5, 0, CancellationToken.None);

        Assert.Equal("bare-identifier", Assert.Single(hits).Id);
    }

    [Fact]
    public async Task Arxiv_Fetch_NoEntry_Throws()
    {
        const string Empty = """<feed xmlns="http://www.w3.org/2005/Atom"></feed>""";

        var arxiv = new ArxivSource();
        await Assert.ThrowsAsync<BadArgument>(() =>
            arxiv.FetchAsync(Client(_ => Xml(Empty)), "2401.00001", CancellationToken.None));
    }

    [Fact]
    public async Task Arxiv_Fetch_BuildsMetadataWithOptionalComment()
    {
        const string Feed = """
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:arxiv="http://arxiv.org/schemas/atom">
              <entry>
                <id>http://arxiv.org/abs/2401.00001</id>
                <title>Title</title>
                <summary>Abstract body</summary>
                <author><name>Ada Lovelace</name></author>
                <author><name>Alan Turing</name></author>
                <category term="cs.LG" />
                <arxiv:comment>12 pages</arxiv:comment>
              </entry>
            </feed>
            """;

        var arxiv = new ArxivSource();
        var result = await arxiv.FetchAsync(Client(_ => Xml(Feed)), "2401.00001", CancellationToken.None);

        Assert.Equal("2401.00001", result.Id);
        Assert.Equal("Title", result.Title);
        Assert.Equal("Abstract body", result.Preview);
        Assert.Contains("Authors: Ada Lovelace, Alan Turing", result.MetadataLines);
        Assert.Contains("Categories: cs.LG", result.MetadataLines);
        Assert.Contains("Comment: 12 pages", result.MetadataLines);
    }

    [Fact]
    public async Task PubMed_Search_EmptyIdList_ReturnsEmptyWithoutSummaryCall()
    {
        const string SearchJson = """{"esearchresult":{"idlist":[]}}""";
        var summaryCalled = false;

        var pubmed = new PubMedSource();
        var http = Client(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("esummary"))
            {
                summaryCalled = true;
            }

            return Json(SearchJson);
        });

        var hits = await pubmed.SearchAsync(http, "q", 10, 0, CancellationToken.None);

        Assert.Empty(hits);
        Assert.False(summaryCalled);
    }

    [Fact]
    public async Task PubMed_Search_MissingEsearchResult_ShortCircuitsEmpty()
    {
        const string SearchJson = """{"unexpected":true}""";

        var pubmed = new PubMedSource();
        var hits = await pubmed.SearchAsync(Client(_ => Json(SearchJson)), "q", 10, 0, CancellationToken.None);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task PubMed_Search_BuildsHits_PresentAndAbsentEntries()
    {
        const string SearchJson = """{"esearchresult":{"idlist":["111","222"]}}""";
        const string SummaryJson = """
            {"result":{"111":{"title":"First","source":"Journal A","pubdate":"2023"}}}
            """;

        var pubmed = new PubMedSource();
        var http = Client(req =>
            req.RequestUri!.AbsoluteUri.Contains("esummary") ? Json(SummaryJson) : Json(SearchJson));

        var hits = await pubmed.SearchAsync(http, "q", 10, 0, CancellationToken.None);

        Assert.Equal(2, hits.Count);
        Assert.Equal("111", hits[0].Id);
        Assert.Equal("First", hits[0].Title);
        Assert.Equal("Journal A 2023", hits[0].Summary);
        Assert.Equal("222", hits[1].Id);
        Assert.Equal(string.Empty, hits[1].Title);
        Assert.Equal(string.Empty, hits[1].Summary);
    }

    [Fact]
    public async Task PubMed_Search_NoResultObject_FallsBackToBareIds()
    {
        const string SearchJson = """{"esearchresult":{"idlist":["333"]}}""";
        const string SummaryJson = """{"header":{"type":"esummary"}}""";

        var pubmed = new PubMedSource();
        var http = Client(req =>
            req.RequestUri!.AbsoluteUri.Contains("esummary") ? Json(SummaryJson) : Json(SearchJson));

        var hits = await pubmed.SearchAsync(http, "q", 10, 0, CancellationToken.None);

        Assert.Equal("333", Assert.Single(hits).Id);
        Assert.Equal(string.Empty, hits[0].Title);
    }

    [Fact]
    public async Task PubMed_Fetch_NoArticle_Throws()
    {
        const string Body = "<eFetchResult></eFetchResult>";

        var pubmed = new PubMedSource();
        await Assert.ThrowsAsync<BadArgument>(() =>
            pubmed.FetchAsync(Client(_ => Xml(Body)), "999", CancellationToken.None));
    }

    [Fact]
    public async Task Gutenberg_Search_RespectsLimitAndMapsAuthors()
    {
        const string Json = """
            {"results":[
              {"id":1,"title":"Book One","authors":[{"name":"Author A"}]},
              {"id":2,"title":"Book Two","authors":[{"name":"Author B"}]}
            ]}
            """;

        var gutenberg = new GutenbergSource();
        var hits = await gutenberg.SearchAsync(Client(_ => ResearchSourcesTests.Json(Json)), "q", 1, 0, CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("1", hit.Id);
        Assert.Equal("Book One", hit.Title);
        Assert.Equal("Author A", hit.Summary);
    }

    [Fact]
    public async Task Gutenberg_Search_MissingResults_ReturnsEmpty()
    {
        const string Json = """{"count":0}""";

        var gutenberg = new GutenbergSource();
        var hits = await gutenberg.SearchAsync(Client(_ => ResearchSourcesTests.Json(Json)), "q", 10, 0, CancellationToken.None);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Gutenberg_ResolveDownload_NonNumericId_Throws()
    {
        var gutenberg = new GutenbergSource();
        await Assert.ThrowsAsync<BadArgument>(() =>
            gutenberg.ResolveDownloadAsync(Client(_ => ResearchSourcesTests.Json("{}")), "not-a-number", null, CancellationToken.None, NullViceLogger.Instance));
    }

    [Fact]
    public async Task Gutenberg_ResolveDownload_SelectsPlainTextAndExtension()
    {
        const string Json = """
            {"id":42,"title":"Book","formats":{"text/plain; charset=utf-8":"https://gutenberg.org/files/42/42-0.txt"}}
            """;

        var gutenberg = new GutenbergSource();
        var target = await gutenberg.ResolveDownloadAsync(Client(_ => ResearchSourcesTests.Json(Json)), "42", null, CancellationToken.None, NullViceLogger.Instance);

        Assert.Equal("txt", target.Extension);
        Assert.Equal("https://gutenberg.org/files/42/42-0.txt", target.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task UniProt_Search_AppliesOffsetAndExtractsName()
    {
        const string Json = """
            {"results":[
              {"primaryAccession":"P0","proteinName":"skip","organism":{"scientificName":"X"}},
              {"primaryAccession":"P1","proteinDescription":{"recommendedName":{"fullName":{"value":"Real Name"}}},"organism":{"scientificName":"Homo sapiens"}}
            ]}
            """;

        var uniprot = new UniProtSource();
        var hits = await uniprot.SearchAsync(Client(_ => ResearchSourcesTests.Json(Json)), "q", 10, 1, CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("P1", hit.Id);
        Assert.Equal("Real Name", hit.Title);
        Assert.Equal("Homo sapiens", hit.Summary);
    }

    [Fact]
    public async Task UniProt_Search_MissingResultsArray_ReturnsEmpty()
    {
        const string Json = """{"results":{"not":"an array"}}""";

        var uniprot = new UniProtSource();
        var hits = await uniprot.SearchAsync(Client(_ => ResearchSourcesTests.Json(Json)), "q", 10, 0, CancellationToken.None);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task AlphaFold_Search_AlwaysThrows()
    {
        var alphafold = new AlphaFoldSource();
        await Assert.ThrowsAsync<BadArgument>(() =>
            alphafold.SearchAsync(Client(_ => ResearchSourcesTests.Json("[]")), "q", 10, 0, CancellationToken.None));
    }

    [Fact]
    public async Task AlphaFold_Fetch_EmptyArray_Throws()
    {
        var alphafold = new AlphaFoldSource();
        await Assert.ThrowsAsync<BadArgument>(() =>
            alphafold.FetchAsync(Client(_ => ResearchSourcesTests.Json("[]")), "P12345", CancellationToken.None));
    }

    [Fact]
    public async Task AlphaFold_Fetch_BuildsMetadataAndUrls()
    {
        const string Json = """
            [{"gene":"BRCA1","organismScientificName":"Homo sapiens","uniprotDescription":"Breast cancer type 1","modelCreatedDate":"2022-01-01","pdbUrl":"https://af/P.pdb","cifUrl":"https://af/P.cif"}]
            """;

        var alphafold = new AlphaFoldSource();
        var result = await alphafold.FetchAsync(Client(_ => ResearchSourcesTests.Json(Json)), "P12345", CancellationToken.None);

        Assert.Equal("Breast cancer type 1", result.Title);
        Assert.Contains("Gene: BRCA1", result.MetadataLines);
        Assert.Contains("PDB: https://af/P.pdb", result.MetadataLines);
        Assert.Contains("mmCIF: https://af/P.cif", result.MetadataLines);
    }

    [Fact]
    public async Task AlphaFold_ResolveDownload_MissingUrlForFormat_Throws()
    {
        const string Json = """[{"pdbUrl":"https://af/P.pdb"}]""";

        var alphafold = new AlphaFoldSource();
        await Assert.ThrowsAsync<BadArgument>(() =>
            alphafold.ResolveDownloadAsync(Client(_ => ResearchSourcesTests.Json(Json)), "P12345", "bcif", CancellationToken.None, NullViceLogger.Instance));
    }
}
