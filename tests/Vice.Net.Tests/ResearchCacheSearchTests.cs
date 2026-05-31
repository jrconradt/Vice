using System.Text;
using Vice.Net.Research;
using Xunit;

namespace Vice.Net.Tests;

[Collection("ResearchCacheSerial")]
public sealed class ResearchCacheSearchTests : IDisposable
{
    private readonly string _cacheHome;
    private readonly string? _priorCacheHome;
    private readonly string? _priorMaxBytes;

    public ResearchCacheSearchTests()
    {
        _cacheHome = Path.Combine(Path.GetTempPath(), $"vice-cache-search-{Guid.NewGuid():N}");
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

    private string SearchFile(string source, string key)
    {
        return Path.Combine(_cacheHome, "research", source, "search", $"{key}.json");
    }

    [Fact]
    public async Task ReadSearch_MissReturnsNull()
    {
        var cache = new ResearchCache();
        var key = ResearchCache.ComputeKey("never-written", 10, 0);

        var hits = await cache.ReadSearchAsync("arxiv", key, CancellationToken.None);

        Assert.Null(hits);
    }

    [Fact]
    public async Task ReadSearch_FreshEntry_RoundTrips()
    {
        var cache = new ResearchCache();
        var key = ResearchCache.ComputeKey("quantum", 10, 0);
        IReadOnlyList<SearchHit> written = new List<SearchHit>
        {
            new("id-1", "Title", "Summary"),
        };
        await cache.WriteSearchAsync("arxiv", key, written, CancellationToken.None);

        var hits = await cache.ReadSearchAsync("arxiv", key, CancellationToken.None);

        Assert.NotNull(hits);
        var hit = Assert.Single(hits!);
        Assert.Equal("id-1", hit.Id);
        Assert.Equal("Title", hit.Title);
    }

    [Fact]
    public async Task ReadSearch_ExpiredEntry_ReturnsNullAndDeletes()
    {
        var cache = new ResearchCache();
        var key = ResearchCache.ComputeKey("expired", 10, 0);
        IReadOnlyList<SearchHit> written = new List<SearchHit>
        {
            new("id-1", "Title", "Summary"),
        };
        await cache.WriteSearchAsync("arxiv", key, written, CancellationToken.None);

        var file = SearchFile("arxiv", key);
        Assert.True(File.Exists(file));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-2));

        var hits = await cache.ReadSearchAsync("arxiv", key, CancellationToken.None);

        Assert.Null(hits);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task ReadSearch_CorruptJson_ReturnsNull()
    {
        var cache = new ResearchCache();
        var key = ResearchCache.ComputeKey("corrupt", 10, 0);

        var file = SearchFile("arxiv", key);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "{not valid json", Encoding.UTF8);

        var hits = await cache.ReadSearchAsync("arxiv", key, CancellationToken.None);

        Assert.Null(hits);
    }
}

[CollectionDefinition("ResearchCacheSerial", DisableParallelization = true)]
public sealed class ResearchCacheSerialCollection { }
