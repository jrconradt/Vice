using System.Collections.Concurrent;
using Vice.Logging;

namespace Vice.Jobs;

internal sealed class JobManager : IJobManager
{
    public const int MAX_RETAINED_JOBS = 1000;
    public const int MAX_LIVE_JOBS = 4096;
    public const int MAX_JOB_ATTEMPTS = 3;
    public const int RETRY_BASE_BACKOFF_MS = 500;

    private readonly IReadOnlyList<IJobRunner> _runners;
    private readonly IViceLogger _logger;
    private readonly TimeSpan _shutdownTimeout;

    private readonly ConcurrentDictionary<int, JobStateHolder> _jobs = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _jobCts = new();
    private readonly ConcurrentDictionary<int, Task> _activeTasks = new();
    private readonly ConcurrentDictionary<int, byte> _terminalSeen = new();
    private readonly ConcurrentQueue<int> _terminalOrder = new();
    private int _nextId;

    private readonly CancellationTokenSource _shutdownCts;
    private readonly JobWorkerPool _workerPool;

    internal event Action<JobState>? JobCompleted;

    internal event Action<JobState, string>? JobFailed;

    internal event Action<JobState, JobProgress>? JobProgressChanged;

    public JobManager(
        IReadOnlyList<IJobRunner> runners,
        int maxConcurrency,
        IViceLogger? logger,
        CancellationToken shutdownLinkedToken)
        : this(runners, maxConcurrency, logger, shutdownLinkedToken, TimeSpan.FromSeconds(10))
    {
    }

    public JobManager(
        IReadOnlyList<IJobRunner> runners,
        int maxConcurrency,
        IViceLogger? logger,
        CancellationToken shutdownLinkedToken,
        TimeSpan shutdownTimeout)
    {
        _logger = logger ?? NullViceLogger.Instance;
        _runners = runners ?? throw new ArgumentNullException(nameof(runners));
        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        _shutdownTimeout = shutdownTimeout;
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownLinkedToken);

        _workerPool = new JobWorkerPool(
            maxConcurrency,
            ExecuteJobAsync,
            _shutdownCts.Token,
            _shutdownTimeout,
            _logger);
    }

    public JobManager(IReadOnlyList<IJobRunner> runners, int maxConcurrency = 3)
        : this(runners, maxConcurrency, null, CancellationToken.None)
    {
    }

    public async Task<int> SubmitAsync(JobDescriptor descriptor, CancellationToken ct)
    {
        if (FindRunner(descriptor.Kind) is null)
        {
            throw new ArgumentException(
                $"No runner registered for job kind '{descriptor.Kind}'.",
                nameof(descriptor));
        }

        EvictOldCompleted();

        var liveCount = _jobs.Count - _terminalOrder.Count;
        if (liveCount >= MAX_LIVE_JOBS)
        {
            throw new InvalidOperationException(
                $"Job submission rejected: {liveCount} non-terminal jobs are retained, at or above the live cap of {MAX_LIVE_JOBS}.");
        }

        var holder = CreateJobState(descriptor);
        var id = holder.Read().Id;

        _jobs.TryAdd(id, holder);

        await _workerPool.EnqueueAsync(holder, _shutdownCts.Token).ConfigureAwait(false);
        return id;
    }

    public async Task PauseAsync(int jobId, CancellationToken ct)
    {
        var holder = FindHolder(jobId);
        if (holder is null)
        {
            return;
        }

        if (!holder.TryTransition(JobStatus.Paused))
        {
            return;
        }

        _jobCts.TryGetValue(jobId, out var cts);
        SafeCancel(cts);

        await DrainActiveTaskAsync(jobId, holder, ct).ConfigureAwait(false);
    }

    private async Task DrainActiveTaskAsync(int jobId, JobStateHolder holder, CancellationToken ct)
    {
        _activeTasks.TryGetValue(jobId, out var activeTask);
        if (activeTask is null)
        {
            return;
        }

        try
        {
            await activeTask.WaitAsync(_shutdownTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.Log(ViceLogLevel.Warn,
                $"job {jobId} pause drain timed out after {_shutdownTimeout}; transitioning to Failed");
            var transitioned = TryTransitionToFailed(holder, "pause drain timed out");
            _activeTasks.TryRemove(jobId, out _);
            _jobCts.TryRemove(jobId, out var stuckCts);
            SafeDispose(stuckCts);
            if (transitioned)
            {
                FailJob(holder, "pause drain timed out");
            }
        }
        catch (Exception ex)
        {
            _logger.Log(ViceLogLevel.Debug, $"job {jobId} pause drain observed exception", ex);
        }
    }

    public async Task ResumeAsync(int jobId, CancellationToken ct)
    {
        var holder = FindHolder(jobId);
        if (holder is null)
        {
            return;
        }

        _activeTasks.TryGetValue(jobId, out var prior);
        if (prior is not null && !prior.IsCompleted)
        {
            _logger.Log(ViceLogLevel.Warn,
                $"job {jobId} resume requested while prior task still in flight; ignoring.");
            return;
        }

        if (!holder.TryTransition(JobStatus.Running))
        {
            return;
        }

        await _workerPool.EnqueueAsync(holder, _shutdownCts.Token).ConfigureAwait(false);
    }

    public Task CancelAsync(int jobId, CancellationToken ct)
    {
        var holder = FindHolder(jobId);
        if (holder is null)
        {
            return Task.CompletedTask;
        }

        var didFail = holder.TryUpdate(s =>
        {
            var afterQueued = s;
            if (s.Status == JobStatus.Queued)
            {
                if (!JobStateTransitions.IsValid(s.Status, JobStatus.Running))
                {
                    return null;
                }

                afterQueued = s with { Status = JobStatus.Running };
            }

            if (!JobStateTransitions.IsValid(afterQueued.Status, JobStatus.Failed))
            {
                return null;
            }

            return afterQueued with
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = "Cancelled",
            };
        });

        if (!didFail)
        {
            return Task.CompletedTask;
        }

        _jobCts.TryGetValue(jobId, out var cts);

        SafeCancel(cts);
        FailJob(holder, "Cancelled");
        return Task.CompletedTask;
    }

    public IReadOnlyList<JobState> GetJobs()
        => _jobs.Values.Select(h => h.Read()).OrderBy(j => j.Id).ToList();

    public WorkerPoolHealth GetWorkerPoolHealth()
        => new(
            _workerPool.ConfiguredConcurrency,
            _workerPool.LiveWorkerCount,
            _workerPool.IsDegraded);

    public JobState? GetJob(int id)
        => FindHolder(id)?.Read();

    public async ValueTask DisposeAsync()
    {
        SafeCancel(_shutdownCts);

        foreach (var cts in _jobCts.Values)
        {
            SafeCancel(cts);
        }

        await _workerPool.DrainAsync().ConfigureAwait(false);

        var taskPairs = _activeTasks.ToArray();
        foreach (var pair in taskPairs)
        {
            var t = pair.Value;
            var id = pair.Key;
            try
            {
                await t.WaitAsync(_shutdownTimeout).ConfigureAwait(false);
                if (t.IsFaulted)
                {
                    _logger.Log(ViceLogLevel.Warn, $"job {id} faulted during shutdown drain", t.Exception);
                }
            }
            catch (TimeoutException)
            {
                _logger.Log(ViceLogLevel.Warn,
                    $"job {id} did not finish within shutdown timeout {_shutdownTimeout}.");
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Warn, $"job {id} drain observed exception", ex);
            }
        }

        var leftoverCts = _jobCts.Values.ToArray();
        _jobCts.Clear();

        foreach (var cts in leftoverCts)
        {
            SafeDispose(cts);
        }

        _shutdownCts.Dispose();
    }

    private JobStateHolder CreateJobState(JobDescriptor descriptor)
    {
        var id = Interlocked.Increment(ref _nextId);

        var state = new JobState
        {
            Id = id,
            Kind = descriptor.Kind,
            Source = descriptor.Source ?? string.Empty,
            ResourceId = descriptor.ResourceId ?? string.Empty,
            DestinationPath = descriptor.DestinationPath ?? string.Empty,
            Format = descriptor.Format,
            Endpoint = descriptor.Endpoint,
            Method = descriptor.Method,
            CreatedAt = DateTime.UtcNow,
        };

        return new JobStateHolder(state);
    }

    private JobStateHolder? FindHolder(int id)
        => _jobs.TryGetValue(id, out var holder) ? holder : null;

    private void RecordTerminal(int jobId)
    {
        if (_terminalSeen.TryAdd(jobId, 0))
        {
            _terminalOrder.Enqueue(jobId);
        }
    }

    private void EvictOldCompleted()
    {
        while (_terminalOrder.Count > MAX_RETAINED_JOBS)
        {
            if (!_terminalOrder.TryDequeue(out var staleId))
            {
                return;
            }

            _terminalSeen.TryRemove(staleId, out _);
            if (_jobs.TryRemove(staleId, out var evicted))
            {
                SweepAbandonedPartial(evicted.Read());
            }
        }
    }

    private void SweepAbandonedPartial(JobState job)
    {
        if (job.Kind != JobKind.Download
            || string.IsNullOrEmpty(job.DestinationPath))
        {
            return;
        }

        var partial = $"{Path.GetFullPath(job.DestinationPath)}.partial";
        try
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Log(ViceLogLevel.Debug, $"abandoned partial cleanup failed: '{partial}'", ex);
        }
    }

    private async Task ExecuteJobAsync(JobStateHolder holder)
    {
        var jobId = holder.Read().Id;
        var execTask = ExecuteJobCoreAsync(holder);
        _activeTasks[jobId] = execTask;
        try
        {
            await execTask.ConfigureAwait(false);
        }
        finally
        {
            _activeTasks.TryRemove(new KeyValuePair<int, Task>(jobId, execTask));
        }
    }

    private async Task ExecuteJobCoreAsync(JobStateHolder holder)
    {
        var snapshot = holder.Read();
        var runner = FindRunner(snapshot.Kind);
        if (runner is null)
        {
            await OnNoRunnerAsync(holder, snapshot.Kind).ConfigureAwait(false);
            return;
        }

        var jobId = snapshot.Id;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        if (!_jobCts.TryAdd(jobId, linkedCts))
        {
            linkedCts.Dispose();
            return;
        }

        var retry = false;
        try
        {
            await runner.RunAsync(holder.Read(), CreateProgressReporter(holder), linkedCts.Token).ConfigureAwait(false);
            await OnSuccessAsync(holder).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await OnCancellationAsync(holder, jobId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            retry = OnFailure(holder, jobId, ex);
        }
        finally
        {
            CleanupJobCts(jobId, linkedCts);
        }

        if (retry)
        {
            await RetryAfterTransientAsync(holder, jobId).ConfigureAwait(false);
        }
    }

    private IProgress<JobProgress> CreateProgressReporter(JobStateHolder holder) =>
        new Progress<JobProgress>(p =>
        {
            var updated = holder.Update(s => s with
            {
                BytesDownloaded = p.BytesDownloaded ?? s.BytesDownloaded,
                TotalBytes = p.TotalBytes ?? s.TotalBytes,
                MessagesReceived = p.MessagesReceived ?? s.MessagesReceived,
            });

            JobProgressChanged?.Invoke(updated, p);
        });

    private Task OnNoRunnerAsync(JobStateHolder holder, JobKind kind)
    {
        var errorMsg = $"No runner found for job kind: {kind}";
        TryTransitionToFailed(holder, errorMsg);
        FailJob(holder, errorMsg);
        return Task.CompletedTask;
    }

    private Task OnSuccessAsync(JobStateHolder holder)
    {
        var transitioned = holder.TryTransition(JobStatus.Completed);
        if (transitioned)
        {
            var completed = holder.Read();
            RecordTerminal(completed.Id);
            EmitCompletedTelemetry(completed);
            JobCompleted?.Invoke(completed);
        }

        return Task.CompletedTask;
    }

    private Task OnCancellationAsync(JobStateHolder holder, int jobId)
    {
        var currentSnap = holder.Read();
        if (currentSnap.Status == JobStatus.Paused)
        {
            _logger.Log(ViceLogLevel.Debug, $"job {jobId} paused (runner drained)");
            return Task.CompletedTask;
        }

        var alreadyFailed = currentSnap.Status == JobStatus.Failed;
        var reason = currentSnap.ErrorMessage ?? "cancelled";
        if (!alreadyFailed && currentSnap.Status != JobStatus.Completed)
        {
            TryTransitionToFailed(holder, reason);
        }

        _logger.Log(ViceLogLevel.Info, $"job {jobId} cancelled");
        if (!alreadyFailed)
        {
            FailJob(holder, reason);
        }

        return Task.CompletedTask;
    }

    private bool OnFailure(JobStateHolder holder, int jobId, Exception ex)
    {
        var current = holder.Read();
        if (IsTransient(ex) && current.Attempt < MAX_JOB_ATTEMPTS - 1
            && !_shutdownCts.IsCancellationRequested
            && current.Status == JobStatus.Running)
        {
            var attempt = holder.Update(s => s with
            {
                Attempt = s.Attempt + 1,
                ErrorMessage = ex.Message,
            }).Attempt;

            _logger.Log(ViceLogLevel.Warn,
                          $"job {jobId} transient failure (attempt {attempt}/{MAX_JOB_ATTEMPTS}); scheduling retry: {ex.Message}");
            return true;
        }

        holder.TryUpdate(s => JobStateTransitions.IsValid(s.Status, JobStatus.Failed)
            ? s with
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message,
            }
            : s with { ErrorMessage = ex.Message });

        _logger.Log(ViceLogLevel.Warn, $"job {jobId} failed: {ex.Message}", ex);
        FailJob(holder, ex.Message);
        return false;
    }

    private async Task RetryAfterTransientAsync(JobStateHolder holder, int jobId)
    {
        var attempt = holder.Read().Attempt;
        var backoff = TimeSpan.FromMilliseconds(RETRY_BASE_BACKOFF_MS * Math.Pow(2, attempt - 1));

        try
        {
            await Task.Delay(backoff, _shutdownCts.Token).ConfigureAwait(false);
            await _workerPool.EnqueueAsync(holder, _shutdownCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var reason = holder.Read().ErrorMessage ?? "cancelled during retry backoff";
            holder.TryUpdate(s => JobStateTransitions.IsValid(s.Status, JobStatus.Failed)
                ? s with
                {
                    Status = JobStatus.Failed,
                    CompletedAt = DateTime.UtcNow,
                    ErrorMessage = reason,
                }
                : null);
            FailJob(holder, reason);
        }
    }

    private static bool IsTransient(Exception ex)
    {
        Exception? current = ex;
        while (current is not null)
        {
            if (current is TimeoutException || current is IOException
                || current.GetType().Name == "SocketException"
                || current.GetType().Name == "HttpRequestException")
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    private void EmitCompletedTelemetry(JobState job)
        => _logger.Log(ViceLogLevel.Info,
                       $"job terminal id={job.Id} kind={job.Kind} status=Completed source={job.Source}");

    private void EmitFailedTelemetry(JobState job, string reason)
        => _logger.Log(ViceLogLevel.Warn,
                       $"job terminal id={job.Id} kind={job.Kind} status=Failed source={job.Source} error={reason}");

    private void FailJob(JobStateHolder holder, string reason)
    {
        var failed = holder.Read();
        if (failed.Status is JobStatus.Failed or JobStatus.Completed)
        {
            RecordTerminal(failed.Id);
        }

        EmitFailedTelemetry(failed, reason);
        JobFailed?.Invoke(failed, reason);
    }

    private static bool TryTransitionToFailed(JobStateHolder holder, string reason) =>
        holder.TryUpdate(s => JobStateTransitions.IsValid(s.Status, JobStatus.Failed)
            ? s with
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = reason,
            }
            : null);

    private void SafeCancel(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException ode)
        {
            Quietly.Swallow(ode, _logger);
        }
    }

    private void SafeDispose(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Dispose();
        }
        catch (ObjectDisposedException ode)
        {
            Quietly.Swallow(ode, _logger);
        }
    }

    private void CleanupJobCts(int jobId, CancellationTokenSource linkedCts)
    {
        CancellationTokenSource? toDispose = null;
        if (_jobCts.TryGetValue(jobId, out var existing) && ReferenceEquals(existing, linkedCts))
        {
            if (_jobCts.TryRemove(new KeyValuePair<int, CancellationTokenSource>(jobId, existing)))
            {
                toDispose = existing;
            }
        }

        SafeDispose(toDispose);
    }

    private IJobRunner? FindRunner(JobKind kind)
    {
        foreach (var runner in _runners)
        {
            if (runner.CanHandle(kind))
            {
                return runner;
            }
        }

        return null;
    }
}
