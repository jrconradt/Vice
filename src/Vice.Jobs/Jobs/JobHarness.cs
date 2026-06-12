using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Vice.Concurrency;
using Vice.Foundation.Execution;
using Vice.Logging;

namespace Vice.Jobs;

public static class JobHarness
{
    public const int PROGRESS_WRITE_INTERVAL_MS = 500;

    public static async Task<int> RunAsync(IReadOnlyList<IJobRunner> runners,
                                           string descriptorJson,
                                           string appName,
                                           IViceLogger? logger,
                                           CancellationToken ct)
    {
        var log = logger ?? NullViceLogger.Instance;

        JobDescriptor? descriptor;
        try
        {
            descriptor = JsonSerializer.Deserialize(descriptorJson, JobJsonContext.Default.JobDescriptor);
        }
        catch (JsonException ex)
        {
            log.Log(ViceLogLevel.Error, "job descriptor is not valid JSON", ex);
            return ViceExitCode.USAGE_ERROR;
        }

        if (descriptor is null)
        {
            log.Log(ViceLogLevel.Error, "job descriptor deserialized to null");
            return ViceExitCode.USAGE_ERROR;
        }

        var root = JobLedger.RootFor(appName);
        var id = Environment.ProcessId;
        using var self = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;
        var state = new JobState
        {
            Id = id,
            Kind = descriptor.Kind,
            Label = descriptor.Label,
            Status = JobStatus.Running,
            Options = new Dictionary<string, string?>(descriptor.Options, StringComparer.Ordinal),
            CreatedAt = now,
            StartedAt = now,
            ProcessStartTimeUtc = self.StartTime.ToUniversalTime(),
        };

        var runner = FindRunner(runners, descriptor.Kind);
        if (runner is null)
        {
            var unrunnable = state with
            {
                Status = JobStatus.Failed,
                ErrorMessage = $"No runner registered for job kind '{descriptor.Kind}'.",
                CompletedAt = DateTime.UtcNow,
            };
            await JobLedger.WriteAsync(root, unrunnable, ct).ConfigureAwait(false);
            log.Log(ViceLogLevel.Error, $"job {id} has no runner for kind '{descriptor.Kind}'");
            return ViceExitCode.FAILURE;
        }

        await JobLedger.WriteAsync(root, state, ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var sighup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, static context => context.Cancel = true);
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cts.Cancel();
        });

        await using var writer = new RecordWriter(root, state, log);

        try
        {
            await runner.RunAsync(state, writer, cts.Token).ConfigureAwait(false);
            await writer.CompleteAsync(JobStatus.Completed, null).ConfigureAwait(false);
            log.Log(ViceLogLevel.Info, $"job terminal id={id} kind={state.Kind} status=Completed label={state.Label}");
            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException)
        {
            await writer.CompleteAsync(JobStatus.Failed, "Cancelled").ConfigureAwait(false);
            log.Log(ViceLogLevel.Info, $"job terminal id={id} kind={state.Kind} status=Failed label={state.Label} error=Cancelled");
            return ViceExitCode.INTERRUPTED;
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(JobStatus.Failed, ex.Message).ConfigureAwait(false);
            log.Log(ViceLogLevel.Warn, $"job terminal id={id} kind={state.Kind} status=Failed label={state.Label} error={ex.Message}", ex);
            return ViceExitCode.FAILURE;
        }
    }

    private static IJobRunner? FindRunner(IReadOnlyList<IJobRunner> runners, JobKind kind)
    {
        foreach (var runner in runners)
        {
            if (runner.CanHandle(kind))
            {
                return runner;
            }
        }

        return null;
    }

    private sealed class RecordWriter : IProgress<JobProgress>, IAsyncDisposable
    {
        private readonly string _root;
        private readonly IViceLogger _logger;
        private readonly SerialQueue _queue;
        private JobState _state;
        private long _lastWriteTick;
        private int _terminal;

        public RecordWriter(string root, JobState initial, IViceLogger logger)
        {
            _root = root;
            _state = initial;
            _logger = logger;
            _queue = new SerialQueue(logger);
            _lastWriteTick = Environment.TickCount64;
        }

        public void Report(JobProgress value)
        {
            if (Volatile.Read(ref _terminal) != 0)
            {
                return;
            }

            JobState updated;
            while (true)
            {
                var current = _state;
                var next = current with
                {
                    ProgressCurrent = value.Current ?? current.ProgressCurrent,
                    ProgressTotal = value.Total ?? current.ProgressTotal,
                    Label = value.Label ?? current.Label,
                    LastProgressAt = DateTime.UtcNow,
                };
                if (Interlocked.CompareExchange(ref _state, next, current) == current)
                {
                    updated = next;
                    break;
                }
            }

            var nowTick = Environment.TickCount64;
            var last = Volatile.Read(ref _lastWriteTick);
            if (nowTick - last < JobHarness.PROGRESS_WRITE_INTERVAL_MS
                || Interlocked.CompareExchange(ref _lastWriteTick, nowTick, last) != last)
            {
                return;
            }

            _ = _queue.EnqueueAsync(token => WriteQuietlyAsync(updated), CancellationToken.None);
        }

        public async Task CompleteAsync(JobStatus status, string? errorMessage)
        {
            Volatile.Write(ref _terminal, 1);
            var terminal = _state with
            {
                Status = status,
                ErrorMessage = errorMessage,
                CompletedAt = DateTime.UtcNow,
            };
            _state = terminal;
            await _queue.EnqueueAsync(token => WriteQuietlyAsync(terminal), CancellationToken.None).ConfigureAwait(false);
        }

        private async Task WriteQuietlyAsync(JobState snapshot)
        {
            try
            {
                await JobLedger.WriteAsync(_root, snapshot, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Debug, $"job {snapshot.Id} record write failed", ex);
            }
        }

        public ValueTask DisposeAsync() => _queue.DisposeAsync();
    }
}
