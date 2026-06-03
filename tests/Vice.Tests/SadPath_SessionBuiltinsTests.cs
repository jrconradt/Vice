using System.Threading.Tasks;
using Vice;
using Vice.Commands;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Foundation.Execution;
using Vice.Jobs;
using Vice.Logging;
using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class SadPath_SessionBuiltinsTests
{
    private const int UnknownId = 9999;

    private static async Task<(CommandExecutor, JobManager, RecordingConsole)> Build()
    {
        var registry = new CommandRegistry();
        var console = new RecordingConsole();
        var jobs = new JobManager(Array.Empty<IJobRunner>());
        var history = new InputHistory();

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, history);
        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance,
            builtins: builtins);

        return await Task.FromResult((executor, jobs, console));
    }

    [Fact]
    public async Task Pause_UnknownId_NoStateChange_ReportsSuccess()
    {
        var (exec, jobs, console) = await Build();
        await using (jobs)
        {
            Assert.Empty(jobs.GetJobs());

            var exit = await exec.ExecuteAsync($"pause {UnknownId}");

            Assert.Equal(ViceExitCode.SUCCESS, exit);
            Assert.Contains($"Job #{UnknownId} paused.", console.Output);
            Assert.Empty(jobs.GetJobs());
            Assert.Null(jobs.GetJob(UnknownId));
            Assert.Equal("", console.Error);
            Assert.DoesNotContain("Exception", console.Error);
        }
    }

    [Fact]
    public async Task Resume_UnknownId_NoStateChange_ReportsSuccess()
    {
        var (exec, jobs, console) = await Build();
        await using (jobs)
        {
            Assert.Empty(jobs.GetJobs());

            var exit = await exec.ExecuteAsync($"resume {UnknownId}");

            Assert.Equal(ViceExitCode.SUCCESS, exit);
            Assert.Contains($"Job #{UnknownId} resumed.", console.Output);
            Assert.Empty(jobs.GetJobs());
            Assert.Null(jobs.GetJob(UnknownId));
            Assert.Equal("", console.Error);
            Assert.DoesNotContain("Exception", console.Error);
        }
    }

    [Fact]
    public async Task Cancel_UnknownId_NoStateChange_ReportsSuccess()
    {
        var (exec, jobs, console) = await Build();
        await using (jobs)
        {
            Assert.Empty(jobs.GetJobs());

            var exit = await exec.ExecuteAsync($"cancel {UnknownId}");

            Assert.Equal(ViceExitCode.SUCCESS, exit);
            Assert.Contains($"Job #{UnknownId} cancelled.", console.Output);
            Assert.Empty(jobs.GetJobs());
            Assert.Null(jobs.GetJob(UnknownId));
            Assert.Equal("", console.Error);
            Assert.DoesNotContain("Exception", console.Error);
        }
    }
}
