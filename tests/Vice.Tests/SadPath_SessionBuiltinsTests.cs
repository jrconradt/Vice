using System.Threading.Tasks;
using Vice;
using Vice.Commands;
using Vice.Jobs;
using Vice.Logging;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class SadPath_SessionBuiltinsTests
{
    private static async Task<(CommandExecutor, JobManager, RecordingConsole, TempDir)> Build()
    {
        var tmp = new TempDir();
        var registry = new CommandRegistry();
        var console = new RecordingConsole();
        var persistence = new JobPersistence(Path.Combine(tmp.Path, "jobs.json"));
        var jobs = new JobManager(Array.Empty<IJobRunner>(), persistence);
        var state = new SessionState(tmp.Path);
        var history = new InputHistory(state.HistoryPath);

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, state, history);
        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None,
            builtins: builtins);

        return await Task.FromResult((executor, jobs, console, tmp));
    }

    [Fact]
    public async Task Pause_UnknownId_NoCrashNoOutputDamage()
    {
        var (exec, jobs, console, tmp) = await Build();
        using (tmp) await using (jobs)
        {
            var exit = await exec.ExecuteAsync("pause 9999");

            Assert.DoesNotContain("Exception", console.Error);
        }
    }

    [Fact]
    public async Task Resume_UnknownId_NoCrashNoOutputDamage()
    {
        var (exec, jobs, console, tmp) = await Build();
        using (tmp) await using (jobs)
        {
            await exec.ExecuteAsync("resume 9999");
            Assert.DoesNotContain("Exception", console.Error);
        }
    }

    [Fact]
    public async Task Cancel_UnknownId_NoCrashNoOutputDamage()
    {
        var (exec, jobs, console, tmp) = await Build();
        using (tmp) await using (jobs)
        {
            await exec.ExecuteAsync("cancel 9999");
            Assert.DoesNotContain("Exception", console.Error);
        }
    }
}
