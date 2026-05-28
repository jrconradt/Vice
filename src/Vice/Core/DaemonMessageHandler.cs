using Vice.Ipc;
using Vice.Jobs;
using Vice.Display;
using Vice.Session;

namespace Vice;

internal sealed class DaemonMessageHandler
{
    private readonly ViceApp _app;
    private readonly JobManager _jobManager;
    private readonly SessionContext _sessionCtx;
    private readonly IConsoleWriter _nullWriter;

    public DaemonMessageHandler(
        ViceApp app,
        JobManager jobManager,
        SessionContext sessionCtx,
        IConsoleWriter nullWriter)
    {
        _app = app;
        _jobManager = jobManager;
        _sessionCtx = sessionCtx;
        _nullWriter = nullWriter;
    }

    public async Task<PipeMessage?> HandleAsync(PipeMessage message, CancellationToken ct)
    {
        if (message is CommandMessage cmd)
        {
            var writer = new CapturingConsoleWriter(_nullWriter);
            var daemonExecutor = _app.CreateDaemonExecutor(_sessionCtx, writer, NullStatusDisplay.Instance);
            var exitCode = await daemonExecutor.ExecuteAsync(cmd.CommandLine, ct).ConfigureAwait(false);
            return new CommandResponse
            {
                ExitCode = exitCode,
                Output = writer.CapturedOutput,
                Error = null
            };
        }

        if (message is JobStatusRequest)
        {
            var jobs = _jobManager.GetJobs();
            var statuses = jobs.Select(j => new JobStatusEntry(
                j.Id,
                j.Kind.ToString(),
                j.Status.ToString(),
                null,
                j.Source ?? j.Method ?? "")).ToList();
            return new JobStatusResponse { Jobs = statuses };
        }

        return null;
    }
}
