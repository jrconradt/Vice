using System.Threading;
using System.Threading.Tasks;
using Vice.Jobs;
using Xunit;

namespace Vice.Tests;

public class JobManagerTests
{
    private sealed class CountingRunner : IJobRunner
    {
        private readonly JobKind _kind;
        private readonly Func<JobState, IProgress<JobProgress>, CancellationToken, Task> _run;
        public int CallCount;

        public CountingRunner(JobKind kind,
            Func<JobState, IProgress<JobProgress>, CancellationToken, Task>? run = null)
        {
            _kind = kind;
            _run = run ?? ((_, _, _) => Task.CompletedTask);
        }

        public bool CanHandle(JobKind kind) => kind == _kind;

        public Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);
            return _run(job, progress, ct);
        }
    }

    [Fact]
    public async Task Submit_AssignsSequentialIds_AndCompletes()
    {
        var completions = new System.Collections.Concurrent.ConcurrentBag<int>();
        var bothCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new CountingRunner(JobKind.Download);
        await using var mgr = new JobManager(new[] { (IJobRunner)runner });

        mgr.JobCompleted += s =>
        {
            completions.Add(s.Id);
            if (completions.Count == 2)
            {
                bothCompleted.TrySetResult();
            }
        };

        var id1 = await mgr.SubmitAsync(JobDescriptor.ForDownload("src", "rid", "/dest", ".txt"), default);
        var id2 = await mgr.SubmitAsync(JobDescriptor.ForDownload("src", "rid", "/dest", ".txt"), default);

        Assert.Equal(1, id1);
        Assert.Equal(2, id2);

        await bothCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var snap = mgr.GetJobs();
        Assert.Equal(2, snap.Count);
    }

    [Fact]
    public async Task Submit_NoMatchingRunner_ThrowsArgumentException()
    {
        await using var mgr = new JobManager(Array.Empty<IJobRunner>());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            mgr.SubmitAsync(JobDescriptor.ForDownload("src", "rid", "/dest", ".txt"), default));

        Assert.Contains("No runner", ex.Message);
    }

    [Fact]
    public async Task Runner_RunsAndCompletes_FiresCompletedEvent()
    {
        var runner = new CountingRunner(JobKind.Download);
        await using var mgr = new JobManager(new[] { (IJobRunner)runner });

        var completed = new TaskCompletionSource<JobState>(TaskCreationOptions.RunContinuationsAsynchronously);
        mgr.JobCompleted += s => completed.TrySetResult(s);

        await mgr.SubmitAsync(JobDescriptor.ForDownload("src", "rid", "/dest", ".txt"), default);
        var job = await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task Cancel_RunningJob_TransitionsToFailed_WithCancelledMessage()
    {
        var runnerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new CountingRunner(JobKind.Download,
            (job, prog, ct) =>
            {
                runnerStarted.TrySetResult();
                return Task.Delay(Timeout.Infinite, ct);
            });

        await using var mgr = new JobManager(new[] { (IJobRunner)runner });

        var failedSignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        mgr.JobFailed += (job, msg) => failedSignal.TrySetResult(msg);

        var id = await mgr.SubmitAsync(JobDescriptor.ForDownload("src", "rid", "/dest", ".txt"), default);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await mgr.CancelAsync(id, default);

        var msg = await failedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Cancelled", msg);
    }

    [Fact]
    public async Task GetJob_ReturnsNull_ForUnknownId()
    {
        await using var mgr = new JobManager(Array.Empty<IJobRunner>());

        Assert.Null(mgr.GetJob(999));
    }
}
