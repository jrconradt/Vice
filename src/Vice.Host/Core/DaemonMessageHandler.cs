using System.Text.Json;
using Vice.Contracts;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Foundation.Execution;
using Vice.Ipc;
using Vice.Jobs;
using Vice.Logging;
using Vice.Parser;
using Vice.Session;

namespace Vice.Host.Core;

internal sealed class DaemonMessageHandler
{
    private readonly ViceApp _app;
    private readonly IJobManager _jobManager;
    private readonly SessionContext _sessionCtx;
    private readonly IConsoleWriter _nullWriter;
    private readonly IViceLogger _logger;
    private readonly IReadOnlySet<string>? _verbAllowlist;
    private readonly DateTime _startedUtc;
    private Func<DaemonLiveness>? _livenessProbe;

    public static readonly IReadOnlySet<string> DaemonControlVerbs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "jobs",
            "status",
            "pause",
            "resume",
            "cancel",
            "history",
            "clear",
        };

    public DaemonMessageHandler(
        ViceApp app,
        IJobManager jobManager,
        SessionContext sessionCtx,
        IConsoleWriter nullWriter,
        IReadOnlySet<string>? verbAllowlist = null)
    {
        _app = app;
        _jobManager = jobManager;
        _sessionCtx = sessionCtx;
        _nullWriter = nullWriter;
        _logger = app.Logger;
        _verbAllowlist = verbAllowlist;
        _startedUtc = DateTime.UtcNow;
    }

    public void BindLiveness(Func<DaemonLiveness> livenessProbe)
    {
        _livenessProbe = livenessProbe;
    }

    public async Task<PipeMessage?> HandleAsync(PipeMessage message, CancellationToken ct)
    {
        if (message is CommandMessage cmd)
        {
            if (!IsAuthorized(cmd.CommandLine, out var deniedVerb))
            {
                _logger.Log(ViceLogLevel.Warn,
                              $"daemon rejected disallowed verb '{deniedVerb}' over IPC");
                return new CommandResponse
                {
                    ExitCode = ViceExitCode.USAGE_ERROR,
                    Output = string.Empty,
                    Error = $"Command '{deniedVerb}' is not permitted over the daemon control channel."
                };
            }

            var writer = new CapturingConsoleWriter(_nullWriter);
            var daemonExecutor = _app.CreateDaemonExecutor(_sessionCtx, writer, NullStatusDisplay.Instance);
            try
            {
                var exitCode = await daemonExecutor.ExecuteAsync(cmd.CommandLine, ct).ConfigureAwait(false);
                return BoundResponse(exitCode, writer.CapturedOutput, null);
            }
            catch (ViceError error)
            {
                var errorText = error.Hint is { } hint
                    ? $"{error}{Environment.NewLine}hint: {hint}"
                    : error.ToString();
                return BoundResponse(error.ExitCode, writer.CapturedOutput, errorText);
            }
        }

        if (message is JobStatusRequest)
        {
            var jobs = _jobManager.GetJobs();
            var statuses = jobs.Select(j => new JobStatusEntry(
                j.Id,
                j.Kind.ToString(),
                j.Status.ToString(),
                ComputeProgress(j),
                j.Label)).ToList();
            return new JobStatusResponse { Jobs = statuses };
        }

        if (message is HealthRequest)
        {
            var poolHealth = _jobManager.GetWorkerPoolHealth();
            var liveness = _livenessProbe?.Invoke() ?? new DaemonLiveness(true,
                                                                          false,
                                                                          null);
            return new HealthResponse
            {
                Version = _app.Version,
                Listening = liveness.Listening,
                AcceptLoopCrashed = liveness.AcceptLoopCrashed,
                FaultSummary = liveness.FaultSummary,
                UptimeSeconds = (DateTime.UtcNow - _startedUtc).TotalSeconds,
                ConfiguredWorkers = poolHealth.ConfiguredConcurrency,
                LiveWorkers = poolHealth.LiveWorkerCount,
                WorkerPoolDegraded = poolHealth.IsDegraded,
                JobCount = _jobManager.GetJobs().Count
            };
        }

        return null;
    }

    private bool IsAuthorized(string commandLine, out string verb)
    {
        verb = string.Empty;
        if (_verbAllowlist is null)
        {
            return true;
        }

        foreach (var stageVerb in StageVerbs(commandLine))
        {
            if (!_verbAllowlist.Contains(stageVerb)
                && !_app.Registry.HasLeadingVerb(stageVerb))
            {
                verb = stageVerb;
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> StageVerbs(string commandLine)
    {
        var tokens = Lexer.Tokenize(commandLine);
        var verbs = new List<string>();
        var stageStarted = false;

        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Word
                && CommandResolver.PipingWords.Contains(token.Value))
            {
                stageStarted = false;
                continue;
            }

            if (stageStarted)
            {
                continue;
            }

            if (token.Kind == TokenKind.GlobalOption
                || token.Kind == TokenKind.CommaSeparator)
            {
                continue;
            }

            verbs.Add(token.Value);
            stageStarted = true;
        }

        if (verbs.Count == 0)
        {
            verbs.Add(string.Empty);
        }

        return verbs;
    }

    private CommandResponse BoundResponse(int exitCode, string output, string? error)
    {
        var candidate = new CommandResponse
        {
            ExitCode = exitCode,
            Output = output,
            Error = error
        };

        if (FitsWithinPipeLimit(candidate))
        {
            return candidate;
        }

        var byteCount = System.Text.Encoding.UTF8.GetByteCount(output);
        _logger.Log(ViceLogLevel.Warn,
                      $"daemon command output of {byteCount} bytes exceeds the {PipeProtocol.MAX_MESSAGE_BYTES}-byte IPC frame limit; returning a recoverable error instead of the full payload");

        return new CommandResponse
        {
            ExitCode = exitCode == ViceExitCode.SUCCESS ? ViceExitCode.FAILURE : exitCode,
            Output = string.Empty,
            Error = $"Command output of {byteCount} bytes exceeds the {PipeProtocol.MAX_MESSAGE_BYTES}-byte daemon IPC frame limit. Re-run the command in a non-daemon session or redirect its output to a file."
        };
    }

    private static bool FitsWithinPipeLimit(CommandResponse response)
    {
        var serialized = JsonSerializer.SerializeToUtf8Bytes(
            (PipeMessage)response,
            PipeMessageJsonContext.Default.PipeMessage);
        return serialized.Length <= PipeProtocol.MAX_MESSAGE_BYTES;
    }

    private static double? ComputeProgress(JobState job)
    {
        if (job.ProgressTotal is { } total
            && total > 0)
        {
            return (double)job.ProgressCurrent / total;
        }

        return null;
    }
}
