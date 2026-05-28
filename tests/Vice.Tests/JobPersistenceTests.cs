using Vice.Jobs;
using Xunit;

namespace Vice.Tests;

public class JobPersistenceTests
{
    [Fact]
    public async Task Save_Then_Load_Roundtrips()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "jobs.json");
        var persistence = new JobPersistence(path);

        var j = new JobState
        {
            Id = 7,
            Kind = JobKind.Download,
            Source = "src",
            ResourceId = "rid",
            DestinationPath = "/x",
            BytesDownloaded = 42,
            TotalBytes = 100,
        };

        await persistence.SaveAsync(new[] { j }, default);
        var loaded = await persistence.LoadAsync(default);

        Assert.Single(loaded);
        Assert.Equal(7, loaded[0].Id);
        Assert.Equal(JobKind.Download, loaded[0].Kind);
        Assert.Equal("src", loaded[0].Source);
        Assert.Equal(42, loaded[0].BytesDownloaded);
        Assert.Equal(100, loaded[0].TotalBytes);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmpty()
    {
        using var tmp = new TempDir();
        var persistence = new JobPersistence(Path.Combine(tmp.Path, "does-not-exist.json"));
        Assert.Empty(await persistence.LoadAsync(default));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsEmpty()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "corrupt.json");
        File.WriteAllText(path, "{ this is not valid json");
        var persistence = new JobPersistence(path);
        Assert.Empty(await persistence.LoadAsync(default));
    }

    [Fact]
    public async Task Save_LeavesNoTempFile()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "jobs.json");
        var persistence = new JobPersistence(path);

        await persistence.SaveAsync(Array.Empty<JobState>(), default);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
