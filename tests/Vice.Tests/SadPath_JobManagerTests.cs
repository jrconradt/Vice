using System.Threading;
using System.Threading.Tasks;
using Vice.Jobs;
using Xunit;

namespace Vice.Tests;

public class SadPath_JobManagerTests
{
    private sealed class DelegatingRunner : IJobRunner
    {
        private readonly Func<JobState, IProgress<JobProgress>, CancellationToken, Task> _run;
        public DelegatingRunner(Func<JobState, IProgress<JobProgress>, CancellationToken, Task> run) => _run = run;
        public bool CanHandle(JobKind kind) => true;
        public Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct) => _run(job, progress, ct);
    }

    [Fact]
    public async Task Runner_ThrowsException_TransitionsToFailed_WithMessage()
    {
        var runner = new DelegatingRunner((_, _, _) => throw new InvalidOperationException("downstream blew up"));
        await using var mgr = new JobManager(new[] { (IJobRunner)runner });

        var failed = new TaskCompletionSource<(JobState, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        mgr.JobFailed += (job, msg) => failed.TrySetResult((job, msg));

        await mgr.SubmitAsync(JobDescriptor.ForDownload("src", "rid", "/dest", ".txt"), default);
        var (job, msg) = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("downstream blew up", msg);
    }

    [Fact]
    public async Task Pause_Then_Resume_Roundtrips()
    {
        var iteration = 0;
        var startedFirstTime = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedSecondTime = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new DelegatingRunner(async (job, prog, ct) =>
        {
            iteration++;
            if (iteration == 1)
            {
                startedFirstTime.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            else
            {
                startedSecondTime.TrySetResult();
            }
        });

        await using var mgr = new JobManager(new[] { (IJobRunner)runner });
        var id = await mgr.SubmitAsync(JobDescriptor.ForDownload("s", "r", "/d", ".x"), default);

        await startedFirstTime.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await mgr.PauseAsync(id, default);
        Assert.Equal(JobStatus.Paused, mgr.GetJob(id)!.Status);

        await mgr.ResumeAsync(id, default);
        await startedSecondTime.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, iteration);
    }

    [Fact]
    public async Task Cancel_QueuedJob_TransitionsToFailed()
    {
        var firstRunnerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new DelegatingRunner(async (job, prog, ct) =>
        {
            firstRunnerStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        await using var mgr = new JobManager(new[] { (IJobRunner)runner }, maxConcurrency: 1);
        await mgr.SubmitAsync(JobDescriptor.ForDownload("a", "a", "/a", ".x"), default);
        await firstRunnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var queuedId = await mgr.SubmitAsync(JobDescriptor.ForDownload("b", "b", "/b", ".x"), default);

        await mgr.CancelAsync(queuedId, default);

        var job = mgr.GetJob(queuedId)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("Cancelled", job.ErrorMessage);
    }

    [Fact]
    public async Task DisposeAsync_CancelsRunningJobs()
    {
        var runnerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new DelegatingRunner(async (job, prog, ct) =>
        {
            runnerStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                runnerExited.TrySetResult();
            }
        });

        var mgr = new JobManager(new[] { (IJobRunner)runner });
        await mgr.SubmitAsync(JobDescriptor.ForDownload("s", "r", "/d", ".x"), default);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await mgr.DisposeAsync();
        await runnerExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pause_UnknownId_DoesNotThrow()
    {
        await using var mgr = new JobManager(Array.Empty<IJobRunner>());

        await mgr.PauseAsync(9999, default);
        await mgr.ResumeAsync(9999, default);
        await mgr.CancelAsync(9999, default);
        Assert.Empty(mgr.GetJobs());
    }
}
