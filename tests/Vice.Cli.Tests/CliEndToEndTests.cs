using Vice.Foundation.Execution;
using Xunit;

namespace Vice.Cli.Tests;

public class CliEndToEndTests
{
    private static readonly IReadOnlyDictionary<string, string> QuietEnv = new Dictionary<string, string>
    {
        ["VICE_LOG_LEVEL"] = "error",
    };

    private static readonly IReadOnlyDictionary<string, string> LoopbackEnv = new Dictionary<string, string>
    {
        ["VICE_LOG_LEVEL"] = "error",
        ["VICE_SAFE_NET_ALLOW"] = "127.0.0.0/8,::1",
        ["VICE_SAFE_NET_ALLOW_HOSTS"] = "localhost",
    };

    [Fact]
    public async Task Vice_NoArgs_EmptyStdin_ExitsSuccess()
    {
        var result = await CliProcess.RunAsync(
            CliProcess.ViceCliDll,
            Array.Empty<string>(),
            stdin: string.Empty,
            environment: QuietEnv);
        Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
    }

    [Fact]
    public async Task Vice_ReplSession_RunsBuiltinsAndExits()
    {
        var script = $"clear{Environment.NewLine}history{Environment.NewLine}exit{Environment.NewLine}";
        var result = await CliProcess.RunAsync(
            CliProcess.ViceCliDll,
            Array.Empty<string>(),
            stdin: script,
            environment: QuietEnv);

        Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);

        var prompts = result.StdOut.Split("vice>").Length - 1;
        Assert.Equal(3, prompts);
    }

    [Fact]
    public async Task Vice_Version_PrintsAndExitsSuccess()
    {
        var result = await CliProcess.RunAsync(
            CliProcess.ViceCliDll,
            new[] { "--version" },
            environment: QuietEnv);
        Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
        Assert.Contains("vice", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Vice_Help_PrintsAndExitsSuccess()
    {
        var result = await CliProcess.RunAsync(
            CliProcess.ViceCliDll,
            new[] { "--help" },
            environment: QuietEnv);
        Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdOut));
    }

    [Fact]
    public async Task Vice_UnknownVerb_ExitsFailure()
    {
        var result = await CliProcess.RunAsync(
            CliProcess.ViceCliDll,
            new[] { "definitely-not-a-real-verb-xyz" },
            environment: QuietEnv);
        Assert.Equal(ViceExitCode.FAILURE, result.ExitCode);
    }

    [Fact]
    public async Task Vice_NonInteractive_NoCommand_ExitsUsageError()
    {
        var result = await CliProcess.RunAsync(
            CliProcess.ViceCliDll,
            new[] { "--non-interactive" },
            environment: QuietEnv);
        Assert.Equal(ViceExitCode.USAGE_ERROR, result.ExitCode);
        Assert.Contains("--non-interactive", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vice_BrokenStdout_ExitsSuccess()
    {
        var result = await CliProcess.RunWithEarlyStdoutCloseAsync(
            CliProcess.ViceCliDll,
            new[] { "--version" });
        Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
    }

    [Fact]
    public async Task ViceMux_Version_ExitsSuccess()
    {
        var result = await CliProcess.RunAsync(
            CliProcess.ViceMuxCliDll,
            new[] { "--version" },
            environment: QuietEnv);
        Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
        Assert.Contains("vice-mux", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Vice_TcpSend_EchoesPayloadAndExitsSuccess()
    {
        await using var server = new LoopbackTcpEchoServer();

        var result = await CliProcess.RunAsync(
            CliProcess.ViceCliDll,
            new[]
            {
                "tcp",
                "send",
                "PING-E2E",
                "to",
                "endpoint",
                $"127.0.0.1:{server.Port}",
            },
            environment: LoopbackEnv);

        Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
        Assert.Contains("PING-E2E", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vice_TcpSend_ConnectionRefused_ExitsFailure()
    {
        var result = await CliProcess.RunAsync(
            CliProcess.ViceCliDll,
            new[]
            {
                "--timeout",
                "1500",
                "tcp",
                "send",
                "unreachable",
                "to",
                "endpoint",
                "127.0.0.1:1",
            },
            environment: QuietEnv);

        Assert.Equal(ViceExitCode.FAILURE, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Fact]
    public async Task ViceMux_UnknownVerb_ExitsFailure()
    {
        var result = await CliProcess.RunAsync(
            CliProcess.ViceMuxCliDll,
            new[] { "definitely-not-a-real-verb-xyz" },
            environment: QuietEnv);
        Assert.Equal(ViceExitCode.FAILURE, result.ExitCode);
    }
}
