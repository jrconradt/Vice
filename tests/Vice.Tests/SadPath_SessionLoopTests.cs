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
    private static (SessionLoop Loop, RecordingConsole Console, InputHistory History, string JobsRoot)
        Build(string input,
              Action<CommandRegistry>? configure = null,
              string? jobsRootOverride = null)
    {
        var registry = new CommandRegistry();
        configure?.Invoke(registry);

        var appName = $"vice-test-{Guid.NewGuid():N}";
        var console = new RecordingConsole();
        var history = new InputHistory();

        SessionBuiltins.RegisterChains(registry,
                                       appName,
                                       Array.Empty<IJobRunner>(),
                                       NullViceLogger.Instance);
        var builtins = new SessionBuiltinRegistry(history);
        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance,
            builtins: builtins);

        var jobsRoot = jobsRootOverride ?? Path.Combine(Path.GetTempPath(), appName);
        var loop = new SessionLoop(executor,
                                   jobsRoot,
                                   history,
                                   console,
                                   new StringReader(input),
                                   prompt: "vice> ");

        return (loop, console, history, jobsRoot);
    }

    [Fact]
    public async Task TerminalJobRecord_IsAnnouncedAtNextPrompt()
    {
        var jobsRoot = Path.Combine(Path.GetTempPath(), $"vice-test-{Guid.NewGuid():N}");
        try
        {
            var (loop, console, _, _) = Build("seed\nexit\n",
                registry => registry.Register(verb("seed"), "seed a failed record",
                    async (ctx, ct) =>
                    {
                        var record = new JobState
                        {
                            Id = 4242,
                            Kind = JobKind.Custom("Download"),
                            Label = "acme/thing",
                            Status = JobStatus.Failed,
                            ErrorMessage = "boom",
                            CompletedAt = DateTime.UtcNow,
                        };
                        await JobLedger.WriteAsync(jobsRoot, record, ct);
                        return 0;
                    }),
                jobsRootOverride: jobsRoot);

            await loop.RunAsync(CancellationToken.None);

            Assert.Contains("Job #4242 failed: acme/thing -- boom", console.Output);
        }
        finally
        {
            if (Directory.Exists(jobsRoot))
            {
                Directory.Delete(jobsRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HandlerException_IsContained_LoopSurvivesAndPrintsError()
    {
        var (loop, console, history, _) = Build("kaboom\nexit\n",
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
        var (loop, _, _, _) = Build("slow\n",
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
