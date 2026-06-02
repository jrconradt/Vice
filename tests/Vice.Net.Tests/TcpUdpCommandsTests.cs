using System.Text;
using Vice.Contracts;
using Vice.Execution;
using Vice.Net.Commands.Network;
using Xunit;

namespace Vice.Net.Tests;

public class TcpUdpCommandsTests
{
    private static CommandContext BuildContext(
        RecordingConsole console,
        string endpoint,
        string data,
        IReadOnlyDictionary<string, string?>? globalOptions = null)
    {
        var targets = new Dictionary<string, string>
        {
            ["endpoint"] = endpoint,
            ["data"] = data,
        };
        return CommandContextFactory.Build(
            console,
            targets,
            globalOptions);
    }

    [Fact]
    public async Task RunTcpAsync_echoes_payload_and_returns_success()
    {
        await using var server = new TcpEchoServer();
        using var console = new RecordingConsole();

        var ctx = BuildContext(console, $"127.0.0.1:{server.Port}", "hello-tcp");

        var exit = await TcpUdpCommands.RunTcpAsync(ctx, "data", CancellationToken.None);

        Assert.Equal(ViceExitCode.SUCCESS, exit);
        Assert.Contains("hello-tcp", console.Output);
        Assert.Empty(console.Error);
    }

    [Fact]
    public async Task RunTcpAsync_returns_failure_on_connection_refused()
    {
        using var console = new RecordingConsole();

        var ctx = BuildContext(
            console,
            "127.0.0.1:1",
            "unreachable",
            new Dictionary<string, string?> { ["timeout"] = "1500" });

        var exit = await TcpUdpCommands.RunTcpAsync(ctx, "data", CancellationToken.None);

        Assert.Equal(ViceExitCode.FAILURE, exit);
        Assert.NotEqual(string.Empty, console.Error);
    }

    [Fact]
    public async Task RunUdpAsync_echoes_datagram_and_returns_success()
    {
        await using var server = new UdpEchoServer();
        using var console = new RecordingConsole();

        var ctx = BuildContext(console, $"127.0.0.1:{server.Port}", "hello-udp");

        var exit = await TcpUdpCommands.RunUdpAsync(ctx, "data", CancellationToken.None);

        Assert.Equal(ViceExitCode.SUCCESS, exit);
        Assert.Contains("hello-udp", console.Output);
    }

    [Fact]
    public async Task RunUdpAsync_with_no_reply_returns_success_without_reading_response()
    {
        await using var server = new UdpEchoServer();
        using var console = new RecordingConsole();

        var ctx = BuildContext(
            console,
            $"127.0.0.1:{server.Port}",
            "fire-and-forget",
            new Dictionary<string, string?> { ["no-reply"] = null });

        var exit = await TcpUdpCommands.RunUdpAsync(ctx, "data", CancellationToken.None);

        Assert.Equal(ViceExitCode.SUCCESS, exit);
        Assert.Equal(string.Empty, console.Output);
    }

    [Fact]
    public async Task RunTcpAsync_round_trips_payload_via_hex_format()
    {
        await using var server = new TcpEchoServer();
        using var console = new RecordingConsole();

        var ctx = BuildContext(
            console,
            $"127.0.0.1:{server.Port}",
            "binary",
            new Dictionary<string, string?> { ["format"] = "hex" });

        var exit = await TcpUdpCommands.RunTcpAsync(ctx, "data", CancellationToken.None);

        Assert.Equal(ViceExitCode.SUCCESS, exit);
        foreach (var b in Encoding.UTF8.GetBytes("binary"))
        {
            Assert.Contains($"{b:X2}", console.Output);
        }

        Assert.Contains("|binary|", console.Output);
    }
}
