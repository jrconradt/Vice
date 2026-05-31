using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vice;
using Vice.Ipc;
using Vice.Jobs;
using Vice.Display;
using Vice.Session;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class DaemonJobStatusProjectionTests
{
    private sealed class BlockingRunner : IJobRunner
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public bool CanHandle(JobKind kind) => kind == JobKind.Download;

        public async Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
        {
            _started.TrySetResult();
            var forever = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => forever.TrySetResult()))
            {
                await forever.Task.ConfigureAwait(false);
            }
        }
    }

    [Fact]
    public async Task RunDaemonAsync_ProjectsSubmittedJob_IntoJobStatusEntry()
    {
        using var tmp = new TempDir();
        var pipeName = "vice-test-daemon-" + Guid.NewGuid().ToString("N");
        var state = new SessionState(tmp.Path, pipeName);

        var runner = new BlockingRunner();
        var app = new ViceApp("vice",
                              "1.0.0",
                              description: null,
                              console: new RecordingConsole(),
                              status: NullStatusDisplay.Instance,
                              jobRunners: new[] { (IJobRunner)runner });

        app.Register(verb("start"), "start a blocking job", async (ctx, ct) =>
        {
            var descriptor = JobDescriptor.ForDownload("acme-source", "rid-7", "/dest/file", ".bin");
            var id = await ctx.Session!.Jobs.SubmitAsync(descriptor, ct);
            ctx.Console.Write(id.ToString());
            return 0;
        });

        using var daemonCts = new CancellationTokenSource();
        var daemonTask = app.RunDaemonAsync(state, daemonCts.Token);

        await using var client = await WaitForClient(pipeName, TimeSpan.FromSeconds(3));
        Assert.NotNull(client);

        var submitResp = await client!.SendAsync(new CommandMessage { CommandLine = "start" }, CancellationToken.None);
        var submitCr = Assert.IsType<CommandResponse>(submitResp);
        Assert.Equal(0, submitCr.ExitCode);
        var submittedId = int.Parse(submitCr.Output);

        await runner.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var statusResp = await client.SendAsync(new JobStatusRequest(), CancellationToken.None);
        var jr = Assert.IsType<JobStatusResponse>(statusResp);

        var entry = Assert.Single(jr.Jobs);
        Assert.Equal(submittedId, entry.Id);
        Assert.Equal("Download", entry.Kind);
        Assert.Equal("acme-source", entry.Label);
        Assert.Contains(entry.Status, new[] { "Queued", "Running" });

        daemonCts.Cancel();
        await daemonTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task<PipeClient?> WaitForClient(string pipeName, TimeSpan budget)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < budget)
        {
            var c = await PipeClient.TryConnectAsync(pipeName, timeoutMs: 200);
            if (c is not null)
            {
                return c;
            }

            await Task.Delay(50);
        }

        return null;
    }
}
