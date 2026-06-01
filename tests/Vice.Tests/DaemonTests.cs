using System.Threading;
using System.Threading.Tasks;
using Vice;
using Vice.Ipc;
using Vice.Display;
using Vice.Session;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class DaemonTests
{
    [Fact]
    public async Task RunDaemonAsync_ServesCommandRoundTrip_OverPipe()
    {
        var pipeName = "vice-test-daemon-" + Guid.NewGuid().ToString("N");
        var state = new SessionState("vice-test", pipeName);

        var app = new ViceApp("vice", "1.0.0", description: null,
            console: new RecordingConsole(), status: NullStatusDisplay.Instance);

        app.Register(verb("ping"), "ping", (ctx, ct) =>
        {
            ctx.Console.Write("pong");
            return Task.FromResult(0);
        });

        using var daemonCts = new CancellationTokenSource();
        var daemonTask = app.RunDaemonAsync(state, daemonCts.Token);

        await using var client = await WaitForClient(pipeName, TimeSpan.FromSeconds(3));
        Assert.NotNull(client);

        var resp = await client!.SendAsync(new CommandMessage { CommandLine = "ping" }, CancellationToken.None);
        var cr = Assert.IsType<CommandResponse>(resp);

        Assert.Equal(0, cr.ExitCode);
        Assert.Equal("pong", cr.Output);

        daemonCts.Cancel();
        await daemonTask;
    }

    [Fact]
    public async Task RunDaemonAsync_HandlesJobStatusRequest()
    {
        var pipeName = "vice-test-daemon-" + Guid.NewGuid().ToString("N");
        var state = new SessionState("vice-test", pipeName);

        var app = new ViceApp("vice", "1.0.0", description: null,
            console: new RecordingConsole(), status: NullStatusDisplay.Instance);

        using var daemonCts = new CancellationTokenSource();
        var daemonTask = app.RunDaemonAsync(state, daemonCts.Token);

        await using var client = await WaitForClient(pipeName, TimeSpan.FromSeconds(3));
        Assert.NotNull(client);

        var resp = await client!.SendAsync(new JobStatusRequest(), CancellationToken.None);
        var jr = Assert.IsType<JobStatusResponse>(resp);
        Assert.Empty(jr.Jobs);

        daemonCts.Cancel();
        await daemonTask;
    }

    [Fact]
    public async Task RunDaemonAsync_StartsPipeServer_AndShutsDownOnCancellation()
    {
        var pipeName = "vice-test-daemon-" + Guid.NewGuid().ToString("N");
        var state = new SessionState("vice-test", pipeName);

        var app = new ViceApp("vice", "1.0.0", description: null,
            console: new RecordingConsole(), status: NullStatusDisplay.Instance);

        using var daemonCts = new CancellationTokenSource();
        var daemonTask = app.RunDaemonAsync(state, daemonCts.Token);

        await using (var client = await WaitForClient(pipeName, TimeSpan.FromSeconds(3)))
        {
            Assert.NotNull(client);
        }

        Assert.False(daemonTask.IsCompleted);

        daemonCts.Cancel();
        var exitCode = await daemonTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, exitCode);

        var afterShutdown = await PipeClient.TryConnectAsync(pipeName, timeoutMs: 200);
        Assert.Null(afterShutdown);
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
