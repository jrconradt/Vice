using System;
using System.Threading;
using System.Threading.Tasks;
using Vice.Contracts;
using Vice.Core;
using Vice.Display;
using Vice.Host;
using Vice.Host.Core;
using Vice.Ipc;
using Vice.Jobs;
using Vice.Logging;
using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class DaemonHealthLivenessTests
{
    private sealed class NoopRunner : IJobRunner
    {
        public bool CanHandle(JobKind kind) => true;

        public Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task WorkerPool_AfterAllWorkersExit_ReportsDegraded()
    {
        var pool = new JobWorkerPool(
            2,
            _ => Task.CompletedTask,
            CancellationToken.None,
            TimeSpan.FromSeconds(5),
            NullViceLogger.Instance);

        Assert.False(pool.IsDegraded);
        Assert.Equal(2, pool.LiveWorkerCount);

        await pool.DrainAsync();

        Assert.True(pool.IsDegraded);
        Assert.Equal(0, pool.LiveWorkerCount);
        Assert.Equal(2, pool.ConfiguredConcurrency);
    }

    [Fact]
    public async Task HealthResponse_ReflectsDegradedPool_AndSurfacesLivenessFault()
    {
        var app = new ViceApp("vice",
                              "9.9.9",
                              description: null,
                              console: new RecordingConsole(),
                              status: NullStatusDisplay.Instance,
                              jobRunners: new[] { (IJobRunner)new NoopRunner() });

        await using var jobManager = new JobManager(new[] { (IJobRunner)new NoopRunner() }, 2);

        var poolField = typeof(JobManager).GetField(
            "_workerPool",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(poolField);
        var pool = (JobWorkerPool)poolField!.GetValue(jobManager)!;
        await pool.DrainAsync();

        Assert.True(jobManager.GetWorkerPoolHealth().IsDegraded);

        var state = new SessionState("vice", "vice-test-health-" + Guid.NewGuid().ToString("N"));
        var sessionCtx = new SessionContext(jobManager, state, null, NullViceLogger.Instance, isInteractive: false);

        var handler = new DaemonMessageHandler(
            app,
            jobManager,
            sessionCtx,
            NullConsoleWriter.Instance,
            DaemonMessageHandler.DaemonControlVerbs);

        handler.BindLiveness(() => new DaemonLiveness(
            true,
            true,
            "InvalidOperationException: accept loop failed"));

        var response = await handler.HandleAsync(new HealthRequest(), CancellationToken.None);

        var health = Assert.IsType<HealthResponse>(response);
        Assert.Equal("9.9.9", health.Version);
        Assert.True(health.WorkerPoolDegraded);
        Assert.Equal(2, health.ConfiguredWorkers);
        Assert.Equal(0, health.LiveWorkers);
        Assert.True(health.AcceptLoopCrashed);
        Assert.Equal("InvalidOperationException: accept loop failed", health.FaultSummary);
    }

    [Fact]
    public void DaemonLiveness_Probe_SurfacesServerFaultIntoFaultSummary()
    {
        var faulted = new InvalidOperationException("bind collision");

        Func<DaemonLiveness> probe = () => new DaemonLiveness(
            false,
            true,
            faulted is { } fault ? $"{fault.GetType().Name}: {fault.Message}" : null);

        var liveness = probe();

        Assert.False(liveness.Listening);
        Assert.True(liveness.AcceptLoopCrashed);
        Assert.Equal("InvalidOperationException: bind collision", liveness.FaultSummary);
    }

    [Fact]
    public async Task StatusCommand_AgainstDegradedDaemon_ReturnsNonZero()
    {
        var appName = "vice-test-statusdeg-" + Guid.NewGuid().ToString("N");
        var pipeName = SessionState.For(appName).PipeName;

        var hostApp = new ViceApp("vice",
                                  "9.9.9",
                                  description: null,
                                  console: new RecordingConsole(),
                                  status: NullStatusDisplay.Instance,
                                  jobRunners: new[] { (IJobRunner)new NoopRunner() });

        await using var jobManager = new JobManager(new[] { (IJobRunner)new NoopRunner() }, 2);

        var poolField = typeof(JobManager).GetField(
            "_workerPool",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var pool = (JobWorkerPool)poolField!.GetValue(jobManager)!;
        await pool.DrainAsync();

        var sessionState = new SessionState(appName, pipeName);
        var sessionCtx = new SessionContext(jobManager, sessionState, null, NullViceLogger.Instance, isInteractive: false);

        var msgHandler = new DaemonMessageHandler(
            hostApp,
            jobManager,
            sessionCtx,
            NullConsoleWriter.Instance,
            DaemonMessageHandler.DaemonControlVerbs);

        msgHandler.BindLiveness(() => new DaemonLiveness(true, false, null));

        await using var server = new PipeServer(pipeName, msgHandler.HandleAsync, NullViceLogger.Instance);
        using var serverCts = new CancellationTokenSource();
        await server.StartAsync(serverCts.Token);

        var clientConsole = new RecordingConsole();
        var clientApp = new ViceApp(appName,
                                    "9.9.9",
                                    description: "test",
                                    console: clientConsole,
                                    status: NullStatusDisplay.Instance);

        var exit = await clientApp.RunAsync(new[] { "status" });

        Assert.NotEqual(0, exit);
        Assert.Contains("unhealthy", clientConsole.Output);
        Assert.Contains("degraded", clientConsole.Output);
    }
}
