using System.Diagnostics;
using Vice.Foundation.Execution;
using Xunit;

namespace Vice.Cli.Tests;

public class DaemonCrossProcessEndToEndTests
{
    private readonly IReadOnlyDictionary<string, string> _env = new Dictionary<string, string>
    {
        ["VICE_LOG_LEVEL"] = "error",
        ["VICE_PIPE_NAME"] = "vice-e2e-daemon-" + Guid.NewGuid().ToString("N"),
    };

    [Fact]
    public async Task ViceDaemon_ChildProcess_ServesStatusClientAndShutsDownCleanly()
    {
        var daemon = StartDaemon();
        try
        {
            var status = await PollForHealthyDaemon(TimeSpan.FromSeconds(20));

            Assert.Equal(ViceExitCode.SUCCESS, status.ExitCode);
            Assert.Contains("vice daemon", status.StdOut, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("healthy", status.StdOut, StringComparison.Ordinal);
            Assert.Contains("listening: yes", status.StdOut, StringComparison.Ordinal);
            Assert.Contains("No jobs.", status.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("No vice daemon running.", status.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            SendTerm(daemon.Id);
        }

        await daemon.WaitForExitAsync();
        Assert.Equal(ViceExitCode.SUCCESS, daemon.ExitCode);

        var afterShutdown = await CliProcess.RunAsync(
            CliProcess.ViceCliDll,
            new[] { "status" },
            environment: _env);

        Assert.Equal(ViceExitCode.SUCCESS, afterShutdown.ExitCode);
        Assert.Contains("No vice daemon running.", afterShutdown.StdOut, StringComparison.Ordinal);
    }

    private Process StartDaemon()
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(CliProcess.ViceCliDll) ?? Environment.CurrentDirectory,
        };
        psi.ArgumentList.Add(CliProcess.ViceCliDll);
        psi.ArgumentList.Add("--daemon");
        foreach (var (key, value) in _env)
        {
            psi.Environment[key] = value;
        }

        var process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException("failed to launch the vice daemon process.");
        }

        process.StandardInput.Close();
        return process;
    }

    private async Task<CliResult> PollForHealthyDaemon(TimeSpan budget)
    {
        var sw = Stopwatch.StartNew();
        CliResult last = new(ViceExitCode.FAILURE, string.Empty, string.Empty);
        while (sw.Elapsed < budget)
        {
            last = await CliProcess.RunAsync(
                CliProcess.ViceCliDll,
                new[] { "status" },
                environment: _env);

            if (last.ExitCode == ViceExitCode.SUCCESS
                && last.StdOut.Contains("healthy", StringComparison.Ordinal)
                && !last.StdOut.Contains("No vice daemon running.", StringComparison.Ordinal))
            {
                return last;
            }

            await Task.Delay(200);
        }

        return last;
    }

    private static void SendTerm(int pid)
    {
        using var kill = Process.Start(new ProcessStartInfo("kill", $"-TERM {pid}")
        {
            UseShellExecute = false,
        });
        kill?.WaitForExit();
    }
}
