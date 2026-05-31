using System.Collections.Concurrent;
using Vice.Jobs;
using Xunit;

namespace Vice.Tests;

public class JobPersistenceDurabilityTests
{
    private sealed class BlockingRunner : IJobRunner
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release;

        public BlockingRunner(TaskCompletionSource release)
        {
            _release = release;
        }

        public Task Started => _started.Task;

        public bool CanHandle(JobKind kind) => kind == JobKind.Download;

        public async Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private sealed class CompletingRunner : IJobRunner
    {
        public bool CanHandle(JobKind kind) => kind == JobKind.Download;

        public Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task Submit_PersistsSynchronously_BeforeDebounceWindow()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "jobs.json");
        var persistence = new JobPersistence(path);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new BlockingRunner(release);

        await using var mgr = new JobManager(new[] { (IJobRunner)runner }, persistence);

        await mgr.SubmitAsync(JobDescriptor.ForDownload("src", "rid", "/dest", ".txt"), default);

        var loaded = await persistence.LoadAsync(default);

        Assert.Single(loaded);
        Assert.Equal(1, loaded[0].Id);

        release.TrySetResult();
    }

    [Fact]
    public async Task Completion_PersistsSynchronously_BeforeDebounceWindow()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "jobs.json");
        var persistence = new JobPersistence(path);
        var runner = new CompletingRunner();

        await using var mgr = new JobManager(new[] { (IJobRunner)runner }, persistence);

        var completed = new TaskCompletionSource<JobState>(TaskCreationOptions.RunContinuationsAsynchronously);
        mgr.JobCompleted += s => completed.TrySetResult(s);

        await mgr.SubmitAsync(JobDescriptor.ForDownload("src", "rid", "/dest", ".txt"), default);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var loaded = await persistence.LoadAsync(default);

        Assert.Single(loaded);
        Assert.Equal(JobStatus.Completed, loaded[0].Status);
    }

    [Fact]
    public async Task Dispose_FlushesLatestSnapshotExactlyOnce()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "jobs.json");
        var persistence = new JobPersistence(path);
        var runner = new CompletingRunner();

        var mgr = new JobManager(new[] { (IJobRunner)runner }, persistence);
        var completed = new TaskCompletionSource<JobState>(TaskCreationOptions.RunContinuationsAsynchronously);
        mgr.JobCompleted += s => completed.TrySetResult(s);

        await mgr.SubmitAsync(JobDescriptor.ForDownload("src", "rid", "/dest", ".txt"), default);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await mgr.DisposeAsync();

        var loaded = await persistence.LoadAsync(default);
        Assert.Single(loaded);
        Assert.Equal(JobStatus.Completed, loaded[0].Status);
    }

    [Fact]
    public async Task Driver_SkipsRedundantWrite_WhenSnapshotUnchanged()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "jobs.json");
        var persistence = new JobPersistence(path);

        var snapshots = new ConcurrentQueue<IReadOnlyList<JobState>>();
        var stable = new List<JobState>
        {
            new() { Id = 1, Kind = JobKind.Download, Status = JobStatus.Completed, Source = "s" },
        };

        await using var driver = new JobPersistenceDriver(
            persistence,
            () => stable,
            CancellationToken.None,
            TimeSpan.FromSeconds(10));

        await driver.FlushNowAsync(default);

        var sentinel = "[]";
        await File.WriteAllTextAsync(path, sentinel);

        await driver.FlushNowAsync(default);

        var after = await File.ReadAllTextAsync(path);
        Assert.Equal(sentinel, after);
    }
}
