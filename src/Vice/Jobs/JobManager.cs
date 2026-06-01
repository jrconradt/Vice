using System.Collections.Concurrent;
using Vice.Logging;

namespace Vice.Jobs;

internal sealed class JobManager : IJobManager
{
    public const int MAX_RETAINED_JOBS = 1000;

    private readonly IReadOnlyList<IJobRunner> _runners;
    private readonly TimeSpan _shutdownTimeout;

    private readonly ConcurrentDictionary<int, JobStateHolder> _jobs = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _jobCts = new();
    private readonly ConcurrentDictionary<int, Task> _activeTasks = new();
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
        _ = logger;
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
            _shutdownTimeout);
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

        var holder = CreateJobState(descriptor);
        var id = holder.Read().Id;

        _jobs.TryAdd(id, holder);
        EvictOldCompleted();

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
            Vice.Log.Emit(ViceLogLevel.Warn,
                $"job {jobId} pause drain timed out after {_shutdownTimeout}; transitioning to Failed");
            var transitioned = TryTransitionToFailed(holder, "pause drain timed out");
            _activeTasks.TryRemove(jobId, out _);
            _jobCts.TryRemove(jobId, out var stuckCts);
            SafeDispose(stuckCts);
            if (transitioned)
            {
                var failed = holder.Read();
                EmitFailedTelemetry(failed, "pause drain timed out");
                JobFailed?.Invoke(failed, "pause drain timed out");
            }
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Debug, $"job {jobId} pause drain observed exception", ex);
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
            Vice.Log.Emit(ViceLogLevel.Warn,
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
        var failed = holder.Read();
        EmitFailedTelemetry(failed, "Cancelled");
        JobFailed?.Invoke(failed, "Cancelled");
        return Task.CompletedTask;
    }

    public IReadOnlyList<JobState> GetJobs()
        => _jobs.Values.Select(h => h.Read()).OrderBy(j => j.Id).ToList();

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
                    Vice.Log.Emit(ViceLogLevel.Warn, $"job {id} faulted during shutdown drain", t.Exception);
                }
            }
            catch (TimeoutException)
            {
                Vice.Log.Emit(ViceLogLevel.Warn,
                    $"job {id} did not finish within shutdown timeout {_shutdownTimeout}.");
            }
            catch (Exception ex)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, $"job {id} drain observed exception", ex);
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
            Endpoint = descriptor.Endpoint,
            Method = descriptor.Method,
            CreatedAt = DateTime.UtcNow,
        };

        return new JobStateHolder(state);
    }

    private JobStateHolder? FindHolder(int id)
        => _jobs.TryGetValue(id, out var holder) ? holder : null;

    private void EvictOldCompleted()
    {
        var completedCount = 0;
        foreach (var holder in _jobs.Values)
        {
            if (holder.Read().Status is JobStatus.Completed or JobStatus.Failed)
            {
                completedCount++;
            }
        }

        var excess = completedCount - MAX_RETAINED_JOBS;
        if (excess <= 0)
        {
            return;
        }

        var completed = _jobs.Values
            .Select(h => h.Read())
            .Where(s => s.Status is JobStatus.Completed or JobStatus.Failed)
            .OrderBy(s => s.CompletedAt ?? DateTime.MinValue)
            .ThenBy(s => s.Id)
            .ToList();

        for (var i = 0; i < excess; i++)
        {
            var stale = completed[i];
            _jobs.TryRemove(stale.Id, out _);
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
            await OnFailureAsync(holder, jobId, ex).ConfigureAwait(false);
        }
        finally
        {
            CleanupJobCts(jobId, linkedCts);
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
        var failed = holder.Read();
        EmitFailedTelemetry(failed, errorMsg);
        JobFailed?.Invoke(failed, errorMsg);
        return Task.CompletedTask;
    }

    private Task OnSuccessAsync(JobStateHolder holder)
    {
        var transitioned = holder.TryTransition(JobStatus.Completed);
        if (transitioned)
        {
            var completed = holder.Read();
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
            Vice.Log.Emit(ViceLogLevel.Debug, $"job {jobId} paused (runner drained)");
            return Task.CompletedTask;
        }

        var alreadyFailed = currentSnap.Status == JobStatus.Failed;
        var reason = currentSnap.ErrorMessage ?? "cancelled";
        if (!alreadyFailed && currentSnap.Status != JobStatus.Completed)
        {
            TryTransitionToFailed(holder, reason);
        }

        Vice.Log.Emit(ViceLogLevel.Info, $"job {jobId} cancelled");
        if (!alreadyFailed)
        {
            var failed = holder.Read();
            EmitFailedTelemetry(failed, reason);
            JobFailed?.Invoke(failed, reason);
        }

        return Task.CompletedTask;
    }

    private Task OnFailureAsync(JobStateHolder holder, int jobId, Exception ex)
    {
        holder.TryUpdate(s => JobStateTransitions.IsValid(s.Status, JobStatus.Failed)
            ? s with
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message,
            }
            : s with { ErrorMessage = ex.Message });

        Vice.Log.Emit(ViceLogLevel.Warn, $"job {jobId} failed: {ex.Message}", ex);
        var failed = holder.Read();
        EmitFailedTelemetry(failed, ex.Message);
        JobFailed?.Invoke(failed, ex.Message);
        return Task.CompletedTask;
    }

    private static void EmitCompletedTelemetry(JobState job)
        => Vice.Log.Emit(ViceLogLevel.Info,
                         $"job terminal id={job.Id} kind={job.Kind} status=Completed source={job.Source}");

    private static void EmitFailedTelemetry(JobState job, string reason)
        => Vice.Log.Emit(ViceLogLevel.Warn,
                         $"job terminal id={job.Id} kind={job.Kind} status=Failed source={job.Source} error={reason}");

    private static bool TryTransitionToFailed(JobStateHolder holder, string reason) =>
        holder.TryUpdate(s => JobStateTransitions.IsValid(s.Status, JobStatus.Failed)
            ? s with
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = reason,
            }
            : null);

    private static void SafeCancel(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException ode)
        {
            System.Diagnostics.Debug.WriteLine(ode);
        }
    }

    private static void SafeDispose(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Dispose();
        }
        catch (ObjectDisposedException ode)
        {
            System.Diagnostics.Debug.WriteLine(ode);
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
