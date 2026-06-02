using System.Threading;
using System.Threading.Tasks;
using Vice;
using Vice.Commands;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Jobs;
using Vice.Logging;
using Vice.Session;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class SadPath_SessionLoopTests
{
    private sealed class StaleRunner : IJobRunner
    {
        public bool CanHandle(JobKind kind) => true;
        public Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
            => Task.Delay(Timeout.Infinite, ct);
    }

    [Fact]
    public async Task ShouldDaemonize_True_WhenActiveJobsRemainOnExit()
    {
        var registry = new CommandRegistry();
        var console = new RecordingConsole();
        await using var jobs = new JobManager(new[] { (IJobRunner)new StaleRunner() });
        var history = new InputHistory();

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, history);

        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance,
            builtins: builtins);

        await jobs.SubmitAsync(JobDescriptor.ForDownload("s", "r", "/d", ".x"), default);
        await Task.Delay(50);

        var loop = new SessionLoop(executor, jobs, history, console,
            new StringReader("exit\n"), prompt: "vice> ");

        var daemonize = await loop.RunAsync(CancellationToken.None);

        Assert.True(daemonize);
        Assert.Contains("active job(s)", console.Output);
    }

    [Fact]
    public async Task HandlerException_IsContained_LoopSurvivesAndPrintsError()
    {
        var registry = new CommandRegistry();
        registry.Register(verb("kaboom"), "boom",
            (ctx, ct) => throw new InvalidOperationException("handler-said-no"));

        var console = new RecordingConsole();
        await using var jobs = new JobManager(Array.Empty<IJobRunner>());
        var history = new InputHistory();

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, history);
        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance,
            builtins: builtins);

        var loop = new SessionLoop(executor, jobs, history, console,
            new StringReader("kaboom\nexit\n"), prompt: "vice> ");

        var daemonize = await loop.RunAsync(CancellationToken.None);

        Assert.False(daemonize);
        Assert.Contains("handler-said-no", console.Error);

        Assert.Equal(new[] { "kaboom", "exit" }, history.GetHistory());
    }

    [Fact]
    public async Task ExternalCancellation_StillEscapesLoop()
    {
        var registry = new CommandRegistry();
        registry.Register(verb("slow"), "slow", async (ctx, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        });

        var console = new RecordingConsole();
        await using var jobs = new JobManager(Array.Empty<IJobRunner>());
        var history = new InputHistory();

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, history);
        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance,
            builtins: builtins);

        var loop = new SessionLoop(executor, jobs, history, console,
            new StringReader("slow\n"), prompt: "vice> ");

        using var cts = new CancellationTokenSource(80);

        var transitionToDaemon = await loop.RunAsync(cts.Token);
        Assert.False(transitionToDaemon);
    }
}
