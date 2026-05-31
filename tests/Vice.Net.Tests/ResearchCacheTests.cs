using Vice.Net.Research;
using Xunit;

namespace Vice.Net.Tests;

public sealed class ResearchCacheTests : IDisposable
{
    private readonly string _cacheHome;
    private readonly string? _priorCacheHome;
    private readonly string? _priorMaxBytes;

    public ResearchCacheTests()
    {
        _cacheHome = Path.Combine(Path.GetTempPath(), $"vice-cache-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cacheHome);
        _priorCacheHome = Environment.GetEnvironmentVariable("VICE_CACHE_HOME");
        _priorMaxBytes = Environment.GetEnvironmentVariable("VICE_CACHE_MAX_BYTES");
        Environment.SetEnvironmentVariable("VICE_CACHE_HOME", _cacheHome);
        Environment.SetEnvironmentVariable("VICE_CACHE_MAX_BYTES", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("VICE_CACHE_HOME", _priorCacheHome);
        Environment.SetEnvironmentVariable("VICE_CACHE_MAX_BYTES", _priorMaxBytes);
        try
        {
            if (Directory.Exists(_cacheHome))
            {
                Directory.Delete(_cacheHome, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task WriteThenReadContent_RoundTripsUnderResearchSubtree()
    {
        var cache = new ResearchCache();
        var payload = new byte[] { 1, 2, 3, 4 };

        await cache.WriteContentAsync(
            "arxiv",
            "2401.00001",
            "pdf",
            "pdf",
            payload,
            CancellationToken.None);

        var path = cache.ReadContentPath(
            "arxiv",
            "2401.00001",
            "pdf",
            "pdf");

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(payload, await File.ReadAllBytesAsync(path!));
        Assert.StartsWith(
            Path.Combine(_cacheHome, "research", "arxiv", "content"),
            path,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReadContentPath_MissReturnsNull()
    {
        var cache = new ResearchCache();
        var path = cache.ReadContentPath(
            "arxiv",
            "missing",
            "pdf",
            "pdf");
        Assert.Null(path);
    }

    [Fact]
    public async Task DifferentFormats_ProduceDistinctEntries()
    {
        var cache = new ResearchCache();

        await cache.WriteContentAsync(
            "arxiv",
            "2401.00001",
            "pdf",
            "pdf",
            new byte[] { 9 },
            CancellationToken.None);
        await cache.WriteContentAsync(
            "arxiv",
            "2401.00001",
            "html",
            "html",
            new byte[] { 8 },
            CancellationToken.None);

        var pdf = cache.ReadContentPath(
            "arxiv",
            "2401.00001",
            "pdf",
            "pdf");
        var html = cache.ReadContentPath(
            "arxiv",
            "2401.00001",
            "html",
            "html");

        Assert.NotNull(pdf);
        Assert.NotNull(html);
        Assert.NotEqual(pdf, html);
    }

    [Fact]
    public async Task ContentBudget_EvictsLeastRecentlyUsed()
    {
        var cache = new ResearchCache(contentBudgetBytes: 1500);
        var oneKilobyte = new byte[1024];

        await cache.WriteContentAsync(
            "arxiv",
            "old",
            "pdf",
            "pdf",
            oneKilobyte,
            CancellationToken.None);

        var oldPath = cache.ReadContentPath(
            "arxiv",
            "old",
            "pdf",
            "pdf");
        Assert.NotNull(oldPath);
        File.SetLastAccessTimeUtc(oldPath!, DateTime.UtcNow.AddHours(-2));

        await cache.WriteContentAsync(
            "arxiv",
            "new",
            "pdf",
            "pdf",
            oneKilobyte,
            CancellationToken.None);

        Assert.False(File.Exists(oldPath));
        Assert.NotNull(cache.ReadContentPath(
            "arxiv",
            "new",
            "pdf",
            "pdf"));
    }

    [Fact]
    public async Task Prune_DropsExpiredSearchEntries()
    {
        var cache = new ResearchCache();
        IReadOnlyList<SearchHit> hits = new List<SearchHit>
        {
            new("id-1", "title", "summary"),
        };

        var key = ResearchCache.ComputeKey("quantum", 10, 0);
        await cache.WriteSearchAsync("arxiv", key, hits, CancellationToken.None);

        var searchFile = Path.Combine(_cacheHome, "research", "arxiv", "search", $"{key}.json");
        Assert.True(File.Exists(searchFile));

        File.SetLastWriteTimeUtc(searchFile, DateTime.UtcNow.AddHours(-2));
        cache.Prune();

        Assert.False(File.Exists(searchFile));
    }

    [Fact]
    public void ComputeKey_IncludesFormatWhenProvided()
    {
        var withoutFormat = ResearchCache.ComputeKey("quantum", 10, 0);
        var withFormat = ResearchCache.ComputeKey("quantum", 10, 0, "pdf");
        var withOtherFormat = ResearchCache.ComputeKey("quantum", 10, 0, "html");

        Assert.NotEqual(withoutFormat, withFormat);
        Assert.NotEqual(withFormat, withOtherFormat);
    }
}
