using System.Threading;
using System.Threading.Tasks;
using Vice.Ipc;
using Vice.Logging;
using Xunit;

namespace Vice.Tests;

public class IpcTests
{
    private static string UniquePipeName() => "vice-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Server_Echoes_CommandMessage_ToClient()
    {
        var pipeName = UniquePipeName();
        var server = new PipeServer(pipeName, (msg, ct) =>
        {
            if (msg is CommandMessage cmd)
            {
                return Task.FromResult<PipeMessage?>(new CommandResponse
                {
                    ExitCode = 0,
                    Output = "echo:" + cmd.CommandLine,
                });
            }
            return Task.FromResult<PipeMessage?>(null);
        }, NullViceLogger.Instance);

        using var serverCts = new CancellationTokenSource();
        await server.StartAsync(serverCts.Token);
        Assert.True(server.IsListening);

        await using var client = await PipeClient.TryConnectAsync(pipeName, timeoutMs: 2000);
        Assert.NotNull(client);

        var resp = await client!.SendAsync(new CommandMessage { CommandLine = "ping" }, CancellationToken.None);

        var cr = Assert.IsType<CommandResponse>(resp);
        Assert.Equal(0, cr.ExitCode);
        Assert.Equal("echo:ping", cr.Output);

        serverCts.Cancel();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Client_TryConnect_OnMissingServer_ReturnsNull()
    {
        var client = await PipeClient.TryConnectAsync("nonexistent-" + Guid.NewGuid().ToString("N"),
            timeoutMs: 100);
        Assert.Null(client);
    }
}
