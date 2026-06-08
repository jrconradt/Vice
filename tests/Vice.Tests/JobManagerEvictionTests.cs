using System.Collections.Concurrent;
using Vice.Jobs;
using Xunit;

namespace Vice.Tests;

public class JobManagerEvictionTests
{
    private static readonly JobKind TestKind = JobKind.Custom("evict-test");

    private static JobDescriptor Descriptor(JobKind kind)
        => new(kind,
               "evict-label",
               new Dictionary<string, string?>(StringComparer.Ordinal));

    private sealed class EvictionCountingRunner : IJobRunner
    {
        private readonly JobKind _kind;
        public int EvictedCount;
        public readonly ConcurrentQueue<int> EvictedIds = new();

        public EvictionCountingRunner(JobKind kind)
        {
            _kind = kind;
        }

        public bool CanHandle(JobKind kind) => kind == _kind;

        public void OnEvicted(JobState job)
        {
            Interlocked.Increment(ref EvictedCount);
            EvictedIds.Enqueue(job.Id);
        }

        public Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class ThrowingEvictionRunner : IJobRunner
    {
        private readonly JobKind _kind;
        public int EvictedCount;

        public ThrowingEvictionRunner(JobKind kind)
        {
            _kind = kind;
        }

        public bool CanHandle(JobKind kind) => kind == _kind;

        public void OnEvicted(JobState job)
        {
            Interlocked.Increment(ref EvictedCount);
            throw new InvalidOperationException($"eviction hook failed for job {job.Id}");
        }

        public Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
            => Task.CompletedTask;
    }

    private static async Task<int> SubmitAndAwaitTerminalAsync(
        JobManager mgr,
        JobKind kind)
    {
        var terminal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<JobState> onCompleted = s => terminal.TrySetResult(s.Id);
        Action<JobState, string> onFailed = (s, _) => terminal.TrySetResult(s.Id);

        mgr.JobCompleted += onCompleted;
        mgr.JobFailed += onFailed;
        try
        {
            var id = await mgr.SubmitAsync(Descriptor(kind), default);
            var terminalId = await terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(id, terminalId);
            return id;
        }
        finally
        {
            mgr.JobCompleted -= onCompleted;
            mgr.JobFailed -= onFailed;
        }
    }

    [Fact]
    public async Task Eviction_FiresOnEvicted_ForOldestJob_WhenCapExceeded()
    {
        const int RETAINED_CAP = 2;
        var runner = new EvictionCountingRunner(TestKind);
        await using var mgr = new JobManager(
            new[] { (IJobRunner)runner },
            3,
            null,
            CancellationToken.None,
            TimeSpan.FromSeconds(10),
            RETAINED_CAP);

        var firstId = await SubmitAndAwaitTerminalAsync(mgr, TestKind);
        for (var i = 0; i < RETAINED_CAP; i++)
        {
            await SubmitAndAwaitTerminalAsync(mgr, TestKind);
        }

        Assert.Equal(0, runner.EvictedCount);

        await SubmitAndAwaitTerminalAsync(mgr, TestKind);

        Assert.Equal(1, runner.EvictedCount);
        Assert.True(runner.EvictedIds.TryDequeue(out var evictedId));
        Assert.Equal(firstId, evictedId);
        Assert.False(runner.EvictedIds.TryDequeue(out _));
        Assert.Null(mgr.GetJob(firstId));
    }

    [Fact]
    public async Task Eviction_FiresOnEvicted_InOldestFirstOrder_AcrossMultipleEvictions()
    {
        const int RETAINED_CAP = 2;
        var runner = new EvictionCountingRunner(TestKind);
        await using var mgr = new JobManager(
            new[] { (IJobRunner)runner },
            3,
            null,
            CancellationToken.None,
            TimeSpan.FromSeconds(10),
            RETAINED_CAP);

        const int SUBMIT_COUNT = RETAINED_CAP + 3;
        const int EXPECTED_EVICTIONS = SUBMIT_COUNT - RETAINED_CAP - 1;
        var submitted = new List<int>();
        for (var i = 0; i < SUBMIT_COUNT; i++)
        {
            submitted.Add(await SubmitAndAwaitTerminalAsync(mgr, TestKind));
        }

        Assert.Equal(EXPECTED_EVICTIONS, runner.EvictedCount);

        var observed = new List<int>();
        while (runner.EvictedIds.TryDequeue(out var id))
        {
            observed.Add(id);
        }

        Assert.Equal(submitted.GetRange(0, EXPECTED_EVICTIONS), observed);
    }

    [Fact]
    public async Task Eviction_IsResilient_WhenOnEvictedThrows()
    {
        const int RETAINED_CAP = 1;
        var runner = new ThrowingEvictionRunner(TestKind);
        await using var mgr = new JobManager(
            new[] { (IJobRunner)runner },
            3,
            null,
            CancellationToken.None,
            TimeSpan.FromSeconds(10),
            RETAINED_CAP);

        var firstId = await SubmitAndAwaitTerminalAsync(mgr, TestKind);
        await SubmitAndAwaitTerminalAsync(mgr, TestKind);

        var survivorId = await SubmitAndAwaitTerminalAsync(mgr, TestKind);

        Assert.True(runner.EvictedCount >= 1);
        Assert.Null(mgr.GetJob(firstId));
        Assert.NotNull(mgr.GetJob(survivorId));
    }
}
