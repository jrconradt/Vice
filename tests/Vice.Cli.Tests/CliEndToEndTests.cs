using Vice.Execution;
using Xunit;

namespace Vice.Cli.Tests;

public class CliEndToEndTests
{
    private static readonly IReadOnlyDictionary<string, string> QuietEnv = new Dictionary<string, string>
    {
        ["VICE_LOG_LEVEL"] = "error",
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
    public async Task ViceMux_UnknownVerb_ExitsFailure()
    {
        var result = await CliProcess.RunAsync(
            CliProcess.ViceMuxCliDll,
            new[] { "definitely-not-a-real-verb-xyz" },
            environment: QuietEnv);
        Assert.Equal(ViceExitCode.FAILURE, result.ExitCode);
    }
}
