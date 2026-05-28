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
        using var tmp = new TempDir();
        var registry = new CommandRegistry();
        var console = new RecordingConsole();
        var persistence = new JobPersistence(Path.Combine(tmp.Path, "jobs.json"));
        await using var jobs = new JobManager(new[] { (IJobRunner)new StaleRunner() }, persistence);
        var state = new SessionState(tmp.Path, pipeName: "vt-" + Guid.NewGuid().ToString("N"));
        var history = new InputHistory(state.HistoryPath);

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, state, history);

        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None,
            builtins: builtins);

        await jobs.SubmitAsync(JobDescriptor.ForDownload("s", "r", "/d", ".x"), default);
        await Task.Delay(50);

        var loop = new SessionLoop(executor, jobs, history, console,
            new StringReader("exit\n"), prompt: "vice> ");

        var daemonize = await loop.RunAsync(CancellationToken.None);

        Assert.True(daemonize);
        Assert.Contains("Detaching", console.Output);
    }

    [Fact]
    public async Task HandlerException_IsContained_LoopSurvivesAndPrintsError()
    {
        using var tmp = new TempDir();
        var registry = new CommandRegistry();
        registry.Register(verb("kaboom"), "boom",
            (ctx, ct) => throw new InvalidOperationException("handler-said-no"));

        var console = new RecordingConsole();
        var persistence = new JobPersistence(Path.Combine(tmp.Path, "jobs.json"));
        await using var jobs = new JobManager(Array.Empty<IJobRunner>(), persistence);
        var state = new SessionState(tmp.Path, pipeName: "vt-" + Guid.NewGuid().ToString("N"));
        var history = new InputHistory(state.HistoryPath);

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, state, history);
        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None,
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
        using var tmp = new TempDir();
        var registry = new CommandRegistry();
        registry.Register(verb("slow"), "slow", async (ctx, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        });

        var console = new RecordingConsole();
        var persistence = new JobPersistence(Path.Combine(tmp.Path, "jobs.json"));
        await using var jobs = new JobManager(Array.Empty<IJobRunner>(), persistence);
        var state = new SessionState(tmp.Path, pipeName: "vt-" + Guid.NewGuid().ToString("N"));
        var history = new InputHistory(state.HistoryPath);

        SessionBuiltins.RegisterChains(registry);
        var builtins = new SessionBuiltinRegistry(jobs, state, history);
        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None,
            builtins: builtins);

        var loop = new SessionLoop(executor, jobs, history, console,
            new StringReader("slow\n"), prompt: "vice> ");

        using var cts = new CancellationTokenSource(80);

        var transitionToDaemon = await loop.RunAsync(cts.Token);
        Assert.False(transitionToDaemon);
        Assert.True(cts.IsCancellationRequested);
    }
}
