using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vice;
using Vice.Contracts;
using Vice.Display;
using Vice.Ipc;
using Vice.Logging;
using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class SadPath_DaemonAndIpcTests
{
    private static string UniquePipe() => "vice-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Daemon_UnknownCommand_ReturnsCapturedError_NonZeroExit()
    {
        var pipeName = UniquePipe();
        var state = new SessionState("vice-test", pipeName);

        var app = new ViceApp("vice", "1.0.0", description: null,
            console: new RecordingConsole(), status: NullStatusDisplay.Instance);

        using var daemonCts = new CancellationTokenSource();
        var daemonTask = app.RunDaemonAsync(state, daemonCts.Token);

        await using var client = await WaitForClient(pipeName);
        Assert.NotNull(client);

        var resp = await client!.SendAsync(
            new CommandMessage { CommandLine = "no-such-verb" }, CancellationToken.None);
        var cr = Assert.IsType<CommandResponse>(resp);

        Assert.NotEqual(0, cr.ExitCode);

        daemonCts.Cancel();
        await daemonTask;
    }

    [Fact]
    public async Task PipeServer_MultipleConcurrentClients_AllGetReplies()
    {
        var pipeName = UniquePipe();
        var server = new PipeServer(pipeName, async (msg, ct) =>
        {
            await Task.Delay(20, ct);
            return msg is CommandMessage cmd
                ? new CommandResponse { ExitCode = 0, Output = cmd.CommandLine }
                : null;
        }, NullViceLogger.Instance);
        using var cts = new CancellationTokenSource();
        await server.StartAsync(cts.Token);

        var clientCount = 8;
        var sends = Enumerable.Range(0, clientCount).Select(async i =>
        {
            await using var c = await WaitForClient(pipeName);
            Assert.NotNull(c);
            var resp = await c!.SendAsync(new CommandMessage { CommandLine = "c" + i }, CancellationToken.None);
            var cr = Assert.IsType<CommandResponse>(resp);
            return cr.Output;
        }).ToArray();

        var outputs = await Task.WhenAll(sends);
        Assert.Equal(clientCount, outputs.Distinct().Count());
        for (int i = 0; i < clientCount; i++)
        {
            Assert.Contains("c" + i, outputs);
        }

        cts.Cancel();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task PipeProtocol_LargePayload_RoundTrips()
    {
        var pipeName = UniquePipe();
        var bigPayload = new string('x', 200_000);
        var server = new PipeServer(pipeName, (msg, ct) =>
        {
            return Task.FromResult<PipeMessage?>(new CommandResponse
            {
                ExitCode = 0,
                Output = bigPayload
            });
        }, NullViceLogger.Instance);
        using var cts = new CancellationTokenSource();
        await server.StartAsync(cts.Token);

        await using var client = await WaitForClient(pipeName);
        Assert.NotNull(client);

        var resp = await client!.SendAsync(new CommandMessage { CommandLine = "x" }, CancellationToken.None);
        var cr = Assert.IsType<CommandResponse>(resp);
        Assert.Equal(bigPayload.Length, cr.Output.Length);

        cts.Cancel();
        await server.DisposeAsync();
    }

    private static async Task<PipeClient?> WaitForClient(string pipeName)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(3))
        {
            var c = await PipeClient.TryConnectAsync(pipeName, timeoutMs: 200);
            if (c is not null)
            {
                return c;
            }

            await Task.Delay(40);
        }
        return null;
    }
}
