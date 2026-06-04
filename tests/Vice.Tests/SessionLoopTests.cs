using System.Threading;
using System.Threading.Tasks;
using Vice.Commands;
using Vice.Core;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Jobs;
using Vice.Logging;
using Vice.Session;
using Xunit;
using static Vice.Core.Dsl;

namespace Vice.Tests;

public class SessionLoopTests
{
    private static (SessionLoop Loop, JobManager Jobs, RecordingConsole Console, InputHistory History)
        Build(string input, Action<CommandRegistry>? configure = null)
    {
        var registry = new CommandRegistry();
        configure?.Invoke(registry);

        var console = new RecordingConsole();
        var jobs = new JobManager(Array.Empty<IJobRunner>());
        var history = new InputHistory();

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, history);

        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance,
            builtins: builtins);

        var loop = new SessionLoop(executor, jobs, history, console,
            new StringReader(input), prompt: "vice> ");

        return (loop, jobs, console, history);
    }

    [Fact]
    public async Task Eof_ExitsCleanly()
    {
        var (loop, jobs, _, _) = Build("");
        await using (jobs)
        {
            var transitionToDaemon = await loop.RunAsync(CancellationToken.None);
            Assert.False(transitionToDaemon);
        }
    }

    [Fact]
    public async Task ExitCommand_StopsLoop()
    {
        var (loop, jobs, console, _) = Build("exit\n");
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
        var (loop, jobs, console, _) = Build("quit\n");
        await using (jobs)
        {
            var transitionToDaemon = await loop.RunAsync(CancellationToken.None);
            Assert.False(transitionToDaemon);

            var promptCount = console.Output.Split("vice>").Length - 1;
            Assert.Equal(1, promptCount);
        }
    }

    [Fact]
    public async Task BlankLine_IsSkipped_AndPromptReprints()
    {
        var (loop, jobs, console, history) = Build("\n\nexit\n");
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
        var (loop, jobs, _, history) = Build("ping\nexit\n",
            registry => registry.Register(verb("ping"), "ping",
                (ctx, ct) => Task.FromResult(0)));

        await using (jobs)
        {
            await loop.RunAsync(CancellationToken.None);
        }
        Assert.Equal(new[] { "ping", "exit" }, history.GetHistory());
    }
}
