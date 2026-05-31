using System.Text.Json;
using Vice.Ipc;
using Vice.Jobs;
using Vice.Display;
using Vice.Execution;
using Vice.Session;
using Vice.Logging;

namespace Vice;

internal sealed class DaemonMessageHandler
{
    private readonly ViceApp _app;
    private readonly JobManager _jobManager;
    private readonly SessionContext _sessionCtx;
    private readonly IConsoleWriter _nullWriter;
    private readonly IReadOnlySet<string>? _verbAllowlist;

    public DaemonMessageHandler(
        ViceApp app,
        JobManager jobManager,
        SessionContext sessionCtx,
        IConsoleWriter nullWriter,
        IReadOnlySet<string>? verbAllowlist = null)
    {
        _app = app;
        _jobManager = jobManager;
        _sessionCtx = sessionCtx;
        _nullWriter = nullWriter;
        _verbAllowlist = verbAllowlist;
    }

    public async Task<PipeMessage?> HandleAsync(PipeMessage message, CancellationToken ct)
    {
        if (message is CommandMessage cmd)
        {
            if (!IsAuthorized(cmd.CommandLine, out var deniedVerb))
            {
                Vice.Log.Emit(ViceLogLevel.Warn,
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
                j.Source ?? j.Method ?? "")).ToList();
            return new JobStatusResponse { Jobs = statuses };
        }

        return null;
    }

    private bool IsAuthorized(string commandLine, out string verb)
    {
        verb = LeadingVerb(commandLine);
        if (_verbAllowlist is null)
        {
            return true;
        }

        return _verbAllowlist.Contains(verb);
    }

    private static string LeadingVerb(string commandLine)
    {
        var span = commandLine.AsSpan().TrimStart();
        var end = 0;
        while (end < span.Length
            && !char.IsWhiteSpace(span[end]))
        {
            end++;
        }

        return span[..end].ToString();
    }

    private static CommandResponse BoundResponse(int exitCode, string output, string? error)
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
        Vice.Log.Emit(ViceLogLevel.Warn,
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
        if (job.Kind == JobKind.Download
            && job.TotalBytes is { } total
            && total > 0)
        {
            return (double)job.BytesDownloaded / total;
        }

        return null;
    }
}
