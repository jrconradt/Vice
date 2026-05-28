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

public class SessionBuiltinsTests
{
    private static (CommandExecutor Exec, CommandRegistry Reg, JobManager Jobs, RecordingConsole Con, TempDir Tmp)
        Build()
    {
        var tmp = new TempDir();
        var registry = new CommandRegistry();
        var console = new RecordingConsole();
        var persistence = new JobPersistence(System.IO.Path.Combine(tmp.Path, "jobs.json"));
        var jobs = new JobManager(Array.Empty<IJobRunner>(), persistence);
        var state = new SessionState(tmp.Path);
        var history = new InputHistory(state.HistoryPath);

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, state, history);

        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None,
            builtins: builtins);

        return (executor, registry, jobs, console, tmp);
    }

    [Fact]
    public async Task ExitBuiltin_ReturnsExitSignal()
    {
        var (exec, _, jobs, _, tmp) = Build();
        using (tmp) await using (jobs)
        {
            var code = await exec.ExecuteAsync("exit");
            Assert.Equal(SessionLoop.EXIT_SIGNAL, code);
        }
    }

    [Fact]
    public async Task QuitBuiltin_AlsoReturnsExitSignal()
    {
        var (exec, _, jobs, _, tmp) = Build();
        using (tmp) await using (jobs)
        {
            Assert.Equal(SessionLoop.EXIT_SIGNAL, await exec.ExecuteAsync("quit"));
        }
    }

    [Fact]
    public async Task JobsBuiltin_NoJobs_PrintsNoJobs()
    {
        var (exec, _, jobs, console, tmp) = Build();
        using (tmp) await using (jobs)
        {
            await exec.ExecuteAsync("jobs");
            Assert.Contains("No jobs", console.Output);
        }
    }

    [Fact]
    public async Task Registers_ExitAndJobsBuiltins()
    {
        var (_, registry, jobs, _, tmp) = Build();
        using (tmp)
        {
            await jobs.DisposeAsync();
        }

        var names = registry.Registrations.Select(r => r.Chain.Name).ToHashSet();
        Assert.Contains("exit", names);
        Assert.Contains("jobs", names);
    }
}
