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
    private static (SessionLoop Loop, RecordingConsole Console, InputHistory History)
        Build(string input, Action<CommandRegistry>? configure = null)
    {
        var registry = new CommandRegistry();
        configure?.Invoke(registry);

        var console = new RecordingConsole();
        var history = new InputHistory();

        SessionBuiltins.RegisterChains(registry,
                                       Array.Empty<IJobRunner>(),
                                       NullViceLogger.Instance);
        var builtins = new SessionBuiltinRegistry(history);

        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance,
            builtins: builtins);

        var loop = new SessionLoop(executor,
                                   history,
                                   console,
                                   new StringReader(input),
                                   prompt: "vice> ");

        return (loop, console, history);
    }

    [Fact]
    public async Task Eof_ExitsCleanly()
    {
        var (loop, _, _) = Build("");
        await loop.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExitCommand_StopsLoop()
    {
        var (loop, console, _) = Build("exit\n");
        await loop.RunAsync(CancellationToken.None);
        Assert.Contains("vice>", console.Output);
    }

    [Fact]
    public async Task QuitSynonym_AlsoExits()
    {
        var (loop, console, _) = Build("quit\n");
        await loop.RunAsync(CancellationToken.None);

        var promptCount = console.Output.Split("vice>").Length - 1;
        Assert.Equal(1, promptCount);
    }

    [Fact]
    public async Task BlankLine_IsSkipped_AndPromptReprints()
    {
        var (loop, console, history) = Build("\n\nexit\n");
        await loop.RunAsync(CancellationToken.None);

        var promptCount = console.Output.Split("vice>").Length - 1;
        Assert.Equal(3, promptCount);

        Assert.Single(history.GetHistory());
        Assert.Equal("exit", history.GetHistory()[0]);
    }

    [Fact]
    public async Task UserCommand_IsHistoryAppended()
    {
        var (loop, _, history) = Build("ping\nexit\n",
            registry => registry.Register(verb("ping"), "ping",
                (ctx, ct) => Task.FromResult(0)));

        await loop.RunAsync(CancellationToken.None);
        Assert.Equal(new[] { "ping", "exit" }, history.GetHistory());
    }
}
