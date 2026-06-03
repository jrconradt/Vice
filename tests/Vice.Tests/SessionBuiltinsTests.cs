using System.Threading.Tasks;
using Vice.Commands;
using Vice.Core;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Jobs;
using Vice.Logging;
using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class SessionBuiltinsTests
{
    private static (CommandExecutor Exec, CommandRegistry Reg, JobManager Jobs, RecordingConsole Con)
        Build()
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

        return (executor, registry, jobs, console);
    }

    [Fact]
    public async Task ExitBuiltin_ReturnsExitSignal()
    {
        var (exec, _, jobs, _) = Build();
        await using (jobs)
        {
            var code = await exec.ExecuteAsync("exit");
            Assert.Equal(SessionLoop.EXIT_SIGNAL, code);
        }
    }

    [Fact]
    public async Task QuitBuiltin_AlsoReturnsExitSignal()
    {
        var (exec, _, jobs, _) = Build();
        await using (jobs)
        {
            Assert.Equal(SessionLoop.EXIT_SIGNAL, await exec.ExecuteAsync("quit"));
        }
    }

    [Fact]
    public async Task JobsBuiltin_NoJobs_PrintsNoJobs()
    {
        var (exec, _, jobs, console) = Build();
        await using (jobs)
        {
            await exec.ExecuteAsync("jobs");
            Assert.Contains("No jobs", console.Output);
        }
    }

    [Fact]
    public async Task Registers_ExitAndJobsBuiltins()
    {
        var (_, registry, jobs, _) = Build();
        await jobs.DisposeAsync();

        var names = registry.Registrations.Select(r => r.Chain.Name).ToHashSet();
        Assert.Contains("exit", names);
        Assert.Contains("jobs", names);
    }
}
