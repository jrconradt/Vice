using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vice.Jobs;
using Vice.Logging;
using Xunit;

namespace Vice.Tests;

public class JobLedgerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vice-test-ledger-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static JobState Record(int id,
                                   JobStatus status,
                                   DateTime? createdAt = null)
        => new()
        {
            Id = id,
            Kind = JobKind.Custom("Download"),
            Label = $"job-{id}",
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            CompletedAt = status == JobStatus.Running ? null : DateTime.UtcNow,
        };

    [Fact]
    public async Task WriteThenRead_RoundTrips()
    {
        var record = Record(7, JobStatus.Completed) with
        {
            ProgressCurrent = 10,
            ProgressTotal = 20,
            Options = new Dictionary<string, string?>(StringComparer.Ordinal) { ["source"] = "arxiv" },
        };

        await JobLedger.WriteAsync(_root, record, CancellationToken.None);
        var loaded = await JobLedger.ReadAsync(_root, 7, NullViceLogger.Instance, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(7, loaded!.Id);
        Assert.Equal(JobStatus.Completed, loaded.Status);
        Assert.Equal(10, loaded.ProgressCurrent);
        Assert.Equal(20, loaded.ProgressTotal);
        Assert.Equal("arxiv", loaded.Options["source"]);
    }

    [Fact]
    public async Task ReadAll_ReconcilesDeadPidRunningRecord_ToFailed()
    {
        var dead = Record(int.MaxValue - 23, JobStatus.Running) with
        {
            ProcessStartTimeUtc = DateTime.UtcNow,
        };
        await JobLedger.WriteAsync(_root, dead, CancellationToken.None);

        var records = await JobLedger.ReadAllAsync(_root, NullViceLogger.Instance, CancellationToken.None);

        var record = Assert.Single(records);
        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.Contains("process exited", record.ErrorMessage);
    }

    [Fact]
    public async Task ReadAll_KeepsLivePidRunningRecord_AsRunning()
    {
        using var self = Process.GetCurrentProcess();
        var live = Record(Environment.ProcessId, JobStatus.Running) with
        {
            ProcessStartTimeUtc = self.StartTime.ToUniversalTime(),
        };
        await JobLedger.WriteAsync(_root, live, CancellationToken.None);

        var records = await JobLedger.ReadAllAsync(_root, NullViceLogger.Instance, CancellationToken.None);

        var record = Assert.Single(records);
        Assert.Equal(JobStatus.Running, record.Status);
    }

    [Fact]
    public async Task ReadAll_RejectsRecord_WhenPidStartTimeMismatches()
    {
        var reused = Record(Environment.ProcessId, JobStatus.Running) with
        {
            ProcessStartTimeUtc = DateTime.UtcNow.AddDays(-1),
        };
        await JobLedger.WriteAsync(_root, reused, CancellationToken.None);

        var records = await JobLedger.ReadAllAsync(_root, NullViceLogger.Instance, CancellationToken.None);

        var record = Assert.Single(records);
        Assert.Equal(JobStatus.Failed, record.Status);
    }

    [Fact]
    public async Task Prune_DeletesOldestTerminalRecords_BeyondRetention()
    {
        var baseline = DateTime.UtcNow.AddDays(-1);
        for (var i = 1; i <= JobLedger.MAX_RETAINED_RECORDS + 5; i++)
        {
            var record = Record(i, JobStatus.Completed, baseline.AddSeconds(i)) with
            {
                CompletedAt = baseline.AddSeconds(i),
            };
            await JobLedger.WriteAsync(_root, record, CancellationToken.None);
        }

        var records = await JobLedger.ReadAllAsync(_root, NullViceLogger.Instance, CancellationToken.None);
        JobLedger.Prune(_root, records);

        var remaining = await JobLedger.ReadAllAsync(_root, NullViceLogger.Instance, CancellationToken.None);
        Assert.Equal(JobLedger.MAX_RETAINED_RECORDS, remaining.Count);
        Assert.DoesNotContain(remaining, r => r.Id <= 5);
    }

    [Fact]
    public async Task MarkCancelled_WritesTerminalFailedRecord()
    {
        var running = Record(11, JobStatus.Running);
        await JobLedger.WriteAsync(_root, running, CancellationToken.None);

        await JobLedger.MarkCancelledAsync(_root, running, CancellationToken.None);
        var loaded = await JobLedger.ReadAsync(_root, 11, NullViceLogger.Instance, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(JobStatus.Failed, loaded!.Status);
        Assert.Equal("Cancelled", loaded.ErrorMessage);
        Assert.NotNull(loaded.CompletedAt);
    }
}

public class JobSpawnerTests
{
    [Fact]
    public async Task Submit_RejectsDestinationCollision_WithLiveJob()
    {
        var appName = $"vice-test-spawner-{Guid.NewGuid():N}";
        var root = JobLedger.RootFor(appName);
        var destination = Path.Combine(Path.GetTempPath(), $"collide-{Guid.NewGuid():N}.pdf");

        try
        {
            using var self = Process.GetCurrentProcess();
            var live = new JobState
            {
                Id = Environment.ProcessId,
                Kind = JobKind.Custom("Download"),
                Label = "holder",
                Status = JobStatus.Running,
                ProcessStartTimeUtc = self.StartTime.ToUniversalTime(),
                Options = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["destinationPath"] = destination,
                },
            };
            await JobLedger.WriteAsync(root, live, CancellationToken.None);

            var spawner = new JobSpawner(appName, NullViceLogger.Instance, executablePath: "/bin/false");
            var descriptor = new JobDescriptor(JobKind.Custom("Download"),
                                               "challenger",
                                               new Dictionary<string, string?>(StringComparer.Ordinal)
                                               {
                                                   ["destinationPath"] = destination,
                                               });

            var rejection = await Assert.ThrowsAsync<InvalidOperationException>(
                () => spawner.SubmitAsync(descriptor, CancellationToken.None));
            Assert.Contains("already owned by live job", rejection.Message);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
