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

public class SadPath_SessionLoopTests
{
    private static (SessionLoop Loop, RecordingConsole Console, InputHistory History)
        Build(string input,
              Action<CommandRegistry>? configure = null)
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
    public async Task HandlerException_IsContained_LoopSurvivesAndPrintsError()
    {
        var (loop, console, history) = Build("kaboom\nexit\n",
            registry => registry.Register(verb("kaboom"), "boom",
                (ctx, ct) => throw new InvalidOperationException("handler-said-no")));

        await loop.RunAsync(CancellationToken.None);

        Assert.Contains("handler-said-no", console.Error);
        Assert.Equal(new[] { "kaboom", "exit" }, history.GetHistory());
    }

    [Fact]
    public async Task ExternalCancellation_StillEscapesLoop()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (loop, _, _) = Build("slow\n",
            registry => registry.Register(verb("slow"), "slow", async (ctx, ct) =>
            {
                started.TrySetResult();
                var forever = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(() => forever.TrySetResult()))
                {
                    await forever.Task.ConfigureAwait(false);
                }

                ct.ThrowIfCancellationRequested();
                return 0;
            }));

        using var cts = new CancellationTokenSource();
        var loopTask = loop.RunAsync(cts.Token);
        await started.Task;
        cts.Cancel();

        await loopTask;
    }
}
