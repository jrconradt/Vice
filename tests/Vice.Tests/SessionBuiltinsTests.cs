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
    private static (CommandExecutor Exec, CommandRegistry Reg, RecordingConsole Con) Build()
    {
        var registry = new CommandRegistry();
        var console = new RecordingConsole();
        var history = new InputHistory();
        var appName = $"vice-test-{Guid.NewGuid():N}";

        SessionBuiltins.RegisterChains(registry,
                                       appName,
                                       Array.Empty<IJobRunner>(),
                                       NullViceLogger.Instance);
        var builtins = new SessionBuiltinRegistry(history);

        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance,
            builtins: builtins);

        return (executor, registry, console);
    }

    [Fact]
    public async Task ExitBuiltin_ReturnsExitSignal()
    {
        var (exec, _, _) = Build();
        var code = await exec.ExecuteAsync("exit");
        Assert.Equal(SessionLoop.EXIT_SIGNAL, code);
    }

    [Fact]
    public async Task QuitBuiltin_AlsoReturnsExitSignal()
    {
        var (exec, _, _) = Build();
        Assert.Equal(SessionLoop.EXIT_SIGNAL, await exec.ExecuteAsync("quit"));
    }

    [Fact]
    public async Task JobsBuiltin_NoJobs_PrintsNoJobs()
    {
        var (exec, _, console) = Build();
        await exec.ExecuteAsync("jobs");
        Assert.Contains("No jobs", console.Output);
    }

    [Fact]
    public void Registers_ExitJobsAndJobRunBuiltins()
    {
        var (_, registry, _) = Build();

        var names = registry.Registrations.Select(r => r.Chain.Name).ToHashSet();
        Assert.Contains("exit", names);
        Assert.Contains("jobs", names);
        Assert.Contains("job", names);
    }
}
