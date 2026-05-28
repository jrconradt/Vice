using Vice.Jobs;
using Xunit;

namespace Vice.Tests;

public class SadPath_JobPersistenceTests
{
    [Fact]
    public async Task Roundtrip_PreservesAllFields()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "jobs.json");
        var persistence = new JobPersistence(path);

        var j = new JobState
        {
            Id = 11,
            Kind = JobKind.GrpcStream,
            Status = JobStatus.Failed,
            Source = "lab",
            ResourceId = "res-1",
            DestinationPath = "/tmp/x",
            BytesDownloaded = 256,
            TotalBytes = 1024,
            MessagesReceived = 9,
            Endpoint = "grpc.lab.example:443",
            Method = "GreetService/Greet",
            ErrorMessage = "stream reset",
            CreatedAt = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 5, 18, 12, 0, 5, DateTimeKind.Utc),
        };

        await persistence.SaveAsync(new[] { j }, default);
        var loaded = await persistence.LoadAsync(default);

        Assert.Single(loaded);
        var lj = loaded[0];
        Assert.Equal(j.Id, lj.Id);
        Assert.Equal(j.Kind, lj.Kind);
        Assert.Equal(j.Status, lj.Status);
        Assert.Equal(j.Source, lj.Source);
        Assert.Equal(j.ResourceId, lj.ResourceId);
        Assert.Equal(j.DestinationPath, lj.DestinationPath);
        Assert.Equal(j.BytesDownloaded, lj.BytesDownloaded);
        Assert.Equal(j.TotalBytes, lj.TotalBytes);
        Assert.Equal(j.MessagesReceived, lj.MessagesReceived);
        Assert.Equal(j.Endpoint, lj.Endpoint);
        Assert.Equal(j.Method, lj.Method);
        Assert.Equal(j.ErrorMessage, lj.ErrorMessage);
        Assert.Equal(j.CreatedAt, lj.CreatedAt);
        Assert.Equal(j.CompletedAt, lj.CompletedAt);
    }

    [Fact]
    public async Task Save_OverwritesPriorContent()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "jobs.json");
        var persistence = new JobPersistence(path);

        await persistence.SaveAsync(new[] { new JobState { Id = 1, Source = "first" } }, default);
        await persistence.SaveAsync(new[] { new JobState { Id = 2, Source = "second" } }, default);

        var loaded = await persistence.LoadAsync(default);
        Assert.Single(loaded);
        Assert.Equal(2, loaded[0].Id);
        Assert.Equal("second", loaded[0].Source);
    }
}
