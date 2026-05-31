using System.Collections.Concurrent;
using Vice.Logging;

namespace Vice.Jobs;

internal sealed class JobManager : IJobManager
{
    public const int MAX_RETAINED_JOBS = 1000;

    private readonly IReadOnlyList<IJobRunner> _runners;
    private readonly JobPersistence _persistence;
    private readonly TimeSpan _shutdownTimeout;

    private readonly ConcurrentDictionary<int, JobStateHolder> _jobs = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _jobCts = new();
    private readonly ConcurrentDictionary<int, Task> _activeTasks = new();
    private int _nextId;

    private readonly CancellationTokenSource _shutdownCts;
    private readonly JobWorkerPool _workerPool;
    private readonly JobPersistenceDriver _persistenceDriver;

    internal event Action<JobState>? JobCompleted;

    internal event Action<JobState, string>? JobFailed;

    internal event Action<JobState, JobProgress>? JobProgressChanged;

    public JobManager(
        IReadOnlyList<IJobRunner> runners,
        JobPersistence persistence,
        int maxConcurrency,
        IViceLogger? logger,
        CancellationToken shutdownLinkedToken)
        : this(runners, persistence, maxConcurrency, logger, shutdownLinkedToken, TimeSpan.FromSeconds(10))
    {
    }

    public JobManager(
        IReadOnlyList<IJobRunner> runners,
        JobPersistence persistence,
        int maxConcurrency,
        IViceLogger? logger,
        CancellationToken shutdownLinkedToken,
        TimeSpan shutdownTimeout)
    {
        _ = logger;
        _runners = runners ?? throw new ArgumentNullException(nameof(runners));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
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

        _persistenceDriver = new JobPersistenceDriver(
            _persistence,
            SnapshotJobs,
            _shutdownCts.Token,
            _shutdownTimeout);
    }

    public JobManager(IReadOnlyList<IJobRunner> runners, JobPersistence persistence, int maxConcurrency = 3)
        : this(runners, persistence, maxConcurrency, null, CancellationToken.None)
    {
    }

    public static async Task<JobManager> CreateAsync(
        IReadOnlyList<IJobRunner> runners,
        JobPersistence persistence,
        int maxConcurrency,
        IViceLogger? logger,
        CancellationToken shutdownLinkedToken,
        TimeSpan shutdownTimeout)
    {
        var mgr = new JobManager(runners, persistence, maxConcurrency, logger, shutdownLinkedToken, shutdownTimeout);
        await mgr.LoadFromPersistenceAsync(CancellationToken.None).ConfigureAwait(false);
        return mgr;
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

        await FlushCriticalAsync().ConfigureAwait(false);
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

        _persistenceDriver.SignalDirty();

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
            await FlushCriticalAsync().ConfigureAwait(false);
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

        _persistenceDriver.SignalDirty();
        await _workerPool.EnqueueAsync(holder, _shutdownCts.Token).ConfigureAwait(false);
    }

    public async Task CancelAsync(int jobId, CancellationToken ct)
    {
        var holder = FindHolder(jobId);
        if (holder is null)
        {
            return;
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
            return;
        }

        _jobCts.TryGetValue(jobId, out var cts);

        await FlushCriticalAsync().ConfigureAwait(false);
        SafeCancel(cts);
        var failed = holder.Read();
        EmitFailedTelemetry(failed, "Cancelled");
        JobFailed?.Invoke(failed, "Cancelled");
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

        await _persistenceDriver.DrainAndFlushAsync(SnapshotJobs(), CancellationToken.None).ConfigureAwait(false);

        await _persistenceDriver.DisposeAsync().ConfigureAwait(false);

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

    private List<JobState> SnapshotJobs()
        => _jobs.Values.Select(h => h.Read()).OrderBy(s => s.Id).ToList();

    private async Task LoadFromPersistenceAsync(CancellationToken ct)
    {
        List<JobState> loaded;
        try
        {
            loaded = await _persistence.LoadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "job persistence load failed", ex);
            return;
        }

        if (loaded.Count == 0)
        {
            return;
        }

        var mutated = false;
        var requeue = new List<JobStateHolder>();
        foreach (var job in loaded)
        {
            var initial = NormalizeLoaded(job, ref mutated);

            var holder = new JobStateHolder(initial);
            if (!_jobs.TryAdd(initial.Id, holder))
            {
                Vice.Log.Emit(ViceLogLevel.Warn,
                    $"duplicate job id {initial.Id} in jobs.json; keeping first record, dropping duplicate");
                mutated = true;
                continue;
            }

            SeedNextId(initial.Id);

            if (initial.Status == JobStatus.Queued)
            {
                requeue.Add(holder);
            }
            else if (initial.Status == JobStatus.Paused)
            {
                Vice.Log.Emit(ViceLogLevel.Info,
                    $"job {initial.Id} restored in Paused state; issue 'resume' to continue it");
            }
        }

        EvictOldCompleted();

        if (mutated)
        {
            try
            {
                await _persistence.SaveAsync(SnapshotJobs(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, "job persistence save failed", ex);
            }
        }

        foreach (var holder in requeue)
        {
            await _workerPool.EnqueueAsync(holder, _shutdownCts.Token).ConfigureAwait(false);
        }
    }

    private static JobState NormalizeLoaded(JobState job, ref bool mutated)
    {
        var normalized = job;

        if (normalized.Status == JobStatus.Running)
        {
            normalized = normalized with
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = "interrupted by previous shutdown",
            };
            mutated = true;
        }

        var isTerminal = normalized.Status is JobStatus.Completed or JobStatus.Failed;
        if (isTerminal
            && normalized.CompletedAt is null)
        {
            normalized = normalized with { CompletedAt = normalized.CreatedAt };
            mutated = true;
        }
        else if (!isTerminal
            && normalized.CompletedAt is not null)
        {
            normalized = normalized with { CompletedAt = null };
            mutated = true;
        }

        if (normalized.TotalBytes is long total
            && normalized.BytesDownloaded > total)
        {
            normalized = normalized with { BytesDownloaded = total };
            mutated = true;
        }

        return normalized;
    }

    private void SeedNextId(int id)
    {
        var current = Volatile.Read(ref _nextId);
        while (id > current)
        {
            var observed = Interlocked.CompareExchange(ref _nextId, id, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

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

    private async Task OnNoRunnerAsync(JobStateHolder holder, JobKind kind)
    {
        var errorMsg = $"No runner found for job kind: {kind}";
        TryTransitionToFailed(holder, errorMsg);
        await FlushCriticalAsync().ConfigureAwait(false);
        var failed = holder.Read();
        EmitFailedTelemetry(failed, errorMsg);
        JobFailed?.Invoke(failed, errorMsg);
    }

    private async Task OnSuccessAsync(JobStateHolder holder)
    {
        var transitioned = holder.TryTransition(JobStatus.Completed);
        await FlushCriticalAsync().ConfigureAwait(false);
        if (transitioned)
        {
            var completed = holder.Read();
            EmitCompletedTelemetry(completed);
            JobCompleted?.Invoke(completed);
        }
    }

    private async Task OnCancellationAsync(JobStateHolder holder, int jobId)
    {
        var currentSnap = holder.Read();
        if (currentSnap.Status == JobStatus.Paused)
        {
            _persistenceDriver.SignalDirty();
            Vice.Log.Emit(ViceLogLevel.Debug, $"job {jobId} paused (runner drained)");
            return;
        }

        var alreadyFailed = currentSnap.Status == JobStatus.Failed;
        var reason = currentSnap.ErrorMessage ?? "cancelled";
        if (!alreadyFailed && currentSnap.Status != JobStatus.Completed)
        {
            TryTransitionToFailed(holder, reason);
        }

        await FlushCriticalAsync().ConfigureAwait(false);
        Vice.Log.Emit(ViceLogLevel.Info, $"job {jobId} cancelled");
        if (!alreadyFailed)
        {
            var failed = holder.Read();
            EmitFailedTelemetry(failed, reason);
            JobFailed?.Invoke(failed, reason);
        }
    }

    private async Task OnFailureAsync(JobStateHolder holder, int jobId, Exception ex)
    {
        holder.TryUpdate(s => JobStateTransitions.IsValid(s.Status, JobStatus.Failed)
            ? s with
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message,
            }
            : s with { ErrorMessage = ex.Message });

        await FlushCriticalAsync().ConfigureAwait(false);
        Vice.Log.Emit(ViceLogLevel.Warn, $"job {jobId} failed: {ex.Message}", ex);
        var failed = holder.Read();
        EmitFailedTelemetry(failed, ex.Message);
        JobFailed?.Invoke(failed, ex.Message);
    }

    private async Task FlushCriticalAsync()
    {
        try
        {
            await _persistenceDriver.FlushNowAsync(_shutdownCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _persistenceDriver.SignalDirty();
        }
        catch (Exception ex)
        {
            _persistenceDriver.SignalDirty();
            Vice.Log.Emit(ViceLogLevel.Warn, "synchronous job persistence flush failed", ex);
        }
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
