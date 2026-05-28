using System.Threading;
using System.Threading.Tasks;
using Vice;
using Vice.Commands;
using Vice.Jobs;
using Vice.Logging;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Session;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class SessionLoopTests
{
    private static (SessionLoop Loop, JobManager Jobs, RecordingConsole Console, InputHistory History, TempDir Tmp)
        Build(string input, Action<CommandRegistry>? configure = null)
    {
        var tmp = new TempDir();
        var registry = new CommandRegistry();
        configure?.Invoke(registry);

        var console = new RecordingConsole();
        var persistence = new JobPersistence(System.IO.Path.Combine(tmp.Path, "jobs.json"));
        var jobs = new JobManager(Array.Empty<IJobRunner>(), persistence);
        var state = new SessionState(tmp.Path, pipeName: "vice-test-" + Guid.NewGuid().ToString("N"));
        var history = new InputHistory(state.HistoryPath);

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, state, history);

        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None,
            builtins: builtins);

        var loop = new SessionLoop(executor, jobs, history, console,
            new StringReader(input), prompt: "vice> ");

        return (loop, jobs, console, history, tmp);
    }

    [Fact]
    public async Task Eof_ExitsCleanly()
    {
        var (loop, jobs, _, _, tmp) = Build("");
        using (tmp)
        await using (jobs)
        {
            var transitionToDaemon = await loop.RunAsync(CancellationToken.None);
            Assert.False(transitionToDaemon);
        }
    }

    [Fact]
    public async Task ExitCommand_StopsLoop()
    {
        var (loop, jobs, console, _, tmp) = Build("exit\n");
        using (tmp)
        await using (jobs)
        {
            var transitionToDaemon = await loop.RunAsync(CancellationToken.None);
            Assert.False(transitionToDaemon);
            Assert.Contains("vice>", console.Output);
        }
    }

    [Fact]
    public async Task QuitSynonym_AlsoExits()
    {
        var (loop, jobs, _, _, tmp) = Build("quit\n");
        using (tmp)
        await using (jobs)
        {
            await loop.RunAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BlankLine_IsSkipped_AndPromptReprints()
    {
        var (loop, jobs, console, history, tmp) = Build("\n\nexit\n");
        using (tmp)
        await using (jobs)
        {
            await loop.RunAsync(CancellationToken.None);
        }

        var promptCount = console.Output.Split("vice>").Length - 1;
        Assert.Equal(3, promptCount);

        Assert.Single(history.GetHistory());
        Assert.Equal("exit", history.GetHistory()[0]);
    }

    [Fact]
    public async Task UserCommand_IsHistoryAppended()
    {
        var (loop, jobs, _, history, tmp) = Build("ping\nexit\n",
            registry => registry.Register(verb("ping"), "ping",
                (ctx, ct) => Task.FromResult(0)));

        using (tmp)
        await using (jobs)
        {
            await loop.RunAsync(CancellationToken.None);
        }
        Assert.Equal(new[] { "ping", "exit" }, history.GetHistory());
    }
}
