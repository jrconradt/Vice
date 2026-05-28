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

        _persistenceDriver.SignalDirty();
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
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException ode)
        {
            System.Diagnostics.Debug.WriteLine(ode);
        }

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
            try
            {
                stuckCts?.Dispose();
            }
            catch (ObjectDisposedException ode)
            {
                System.Diagnostics.Debug.WriteLine(ode);
            }
            _persistenceDriver.SignalDirty();
            if (transitioned)
            {
                JobFailed?.Invoke(holder.Read(), "pause drain timed out");
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

        _persistenceDriver.SignalDirty();
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException ode)
        {
            System.Diagnostics.Debug.WriteLine(ode);
        }
        JobFailed?.Invoke(holder.Read(), "Cancelled");
        return Task.CompletedTask;
    }

    public IReadOnlyList<JobState> GetJobs()
        => _jobs.Values.Select(h => h.Read()).OrderBy(j => j.Id).ToList();

    public JobState? GetJob(int id)
        => FindHolder(id)?.Read();

    public async ValueTask DisposeAsync()
    {
        try
        {
            _shutdownCts.Cancel();
        }
        catch (ObjectDisposedException ode)
        {
            System.Diagnostics.Debug.WriteLine(ode);
        }

        foreach (var cts in _jobCts.Values)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException ode)
            {
                System.Diagnostics.Debug.WriteLine(ode);
            }
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

        await _persistenceDriver.DisposeAsync().ConfigureAwait(false);

        await _persistenceDriver.FlushAsync(SnapshotJobs(), CancellationToken.None).ConfigureAwait(false);

        var leftoverCts = _jobCts.Values.ToArray();
        _jobCts.Clear();

        foreach (var cts in leftoverCts)
        {
            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException ode)
            {
                System.Diagnostics.Debug.WriteLine(ode);
            }
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

        var resurrected = false;
        foreach (var job in loaded)
        {
            var initial = job;
            if (initial.Status == JobStatus.Running)
            {
                initial = initial with
                {
                    Status = JobStatus.Failed,
                    CompletedAt = DateTime.UtcNow,
                    ErrorMessage = "interrupted by previous shutdown",
                };
                resurrected = true;
            }

            _jobs.TryAdd(initial.Id, new JobStateHolder(initial));
            if (initial.Id > _nextId)
            {
                _nextId = initial.Id;
            }
        }

        EvictOldCompleted();

        if (resurrected)
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
    }

    private void EvictOldCompleted()
    {
        var completed = _jobs.Values
            .Select(h => h.Read())
            .Where(s => s.Status is JobStatus.Completed or JobStatus.Failed)
            .OrderBy(s => s.CompletedAt ?? DateTime.MinValue)
            .ToList();

        var excess = completed.Count - MAX_RETAINED_JOBS;
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
            OnNoRunner(holder, snapshot.Kind);
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
            OnSuccess(holder);
        }
        catch (OperationCanceledException)
        {
            OnCancellation(holder, jobId);
        }
        catch (Exception ex)
        {
            OnFailure(holder, jobId, ex);
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

    private void OnNoRunner(JobStateHolder holder, JobKind kind)
    {
        var errorMsg = $"No runner found for job kind: {kind}";
        TryTransitionToFailed(holder, errorMsg);
        _persistenceDriver.SignalDirty();
        JobFailed?.Invoke(holder.Read(), errorMsg);
    }

    private void OnSuccess(JobStateHolder holder)
    {
        var transitioned = holder.TryTransition(JobStatus.Completed);
        _persistenceDriver.SignalDirty();
        if (transitioned)
        {
            JobCompleted?.Invoke(holder.Read());
        }
    }

    private void OnCancellation(JobStateHolder holder, int jobId)
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

        _persistenceDriver.SignalDirty();
        Vice.Log.Emit(ViceLogLevel.Info, $"job {jobId} cancelled");
        if (!alreadyFailed)
        {
            JobFailed?.Invoke(holder.Read(), reason);
        }
    }

    private void OnFailure(JobStateHolder holder, int jobId, Exception ex)
    {
        holder.TryUpdate(s => JobStateTransitions.IsValid(s.Status, JobStatus.Failed)
            ? s with
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message,
            }
            : s with { ErrorMessage = ex.Message });

        _persistenceDriver.SignalDirty();
        Vice.Log.Emit(ViceLogLevel.Warn, $"job {jobId} failed: {ex.Message}", ex);
        JobFailed?.Invoke(holder.Read(), ex.Message);
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

        try
        {
            toDispose?.Dispose();
        }
        catch (ObjectDisposedException ode)
        {
            System.Diagnostics.Debug.WriteLine(ode);
        }
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
