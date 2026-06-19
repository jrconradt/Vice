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
    private static (ViceApp App, SessionContext Ctx) BuildApp(string appName)
    {
        var app = new ViceApp(appName,
                              "9.9.9",
                              description: null,
                              console: new RecordingConsole(),
                              status: NullStatusDisplay.Instance);

        var state = new SessionState(appName, $"{appName}-pipe");
        var sessionCtx = new SessionContext(new JobSpawner(NullViceLogger.Instance, executablePath: "/bin/false"),
                                            state,
                                            null,
                                            NullViceLogger.Instance,
                                            isInteractive: false);
        return (app, sessionCtx);
    }

    [Fact]
    public async Task HealthResponse_SurfacesLivenessFault()
    {
        var appName = "vice-test-health-" + Guid.NewGuid().ToString("N");
        var (app, sessionCtx) = BuildApp(appName);

        var handler = new DaemonMessageHandler(
            app,
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
    public async Task StatusCommand_AgainstCrashedAcceptLoop_ReturnsNonZero()
    {
        var appName = "vice-test-statusdeg-" + Guid.NewGuid().ToString("N");
        var pipeName = SessionState.For(appName).PipeName;

        var (hostApp, sessionCtx) = BuildApp(appName);

        var msgHandler = new DaemonMessageHandler(
            hostApp,
            sessionCtx,
            NullConsoleWriter.Instance,
            DaemonMessageHandler.DaemonControlVerbs);

        msgHandler.BindLiveness(() => new DaemonLiveness(true,
                                                         true,
                                                         "InvalidOperationException: accept loop failed"));

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
        Assert.Contains("crashed", clientConsole.Output);
    }
}
