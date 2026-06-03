namespace Vice.Jobs;

internal sealed class JobStateHolder
{
    private JobState _state;
    private CancellationTokenSource? _cts;
    private Task? _activeTask;
    private int _terminalRecorded;

    public JobStateHolder(JobState initial)
    {
        _state = initial;
    }

    public JobState Read() => Volatile.Read(ref _state);

    public int Id => Read().Id;

    public Task? ActiveTask => Volatile.Read(ref _activeTask);

    public void SetActiveTask(Task task) => Volatile.Write(ref _activeTask, task);

    public void ClearActiveTask(Task expected)
        => Interlocked.CompareExchange(ref _activeTask, null, expected);

    public bool TryRegisterCts(CancellationTokenSource cts)
        => Interlocked.CompareExchange(ref _cts, cts, null) is null;

    public CancellationTokenSource? Cts => Volatile.Read(ref _cts);

    public CancellationTokenSource? TakeCts(CancellationTokenSource expected)
        => ReferenceEquals(Interlocked.CompareExchange(ref _cts, null, expected), expected)
            ? expected
            : null;

    public CancellationTokenSource? TakeCts()
        => Interlocked.Exchange(ref _cts, null);

    public bool MarkTerminalRecorded()
        => Interlocked.Exchange(ref _terminalRecorded, 1) == 0;

    public bool TryUpdate(Func<JobState, JobState?> mutator)
    {
        JobState current, next;
        do
        {
            current = Volatile.Read(ref _state);
            var result = mutator(current);
            if (result is null)
            {
                return false;
            }

            next = result;
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current));
        return true;
    }

    public JobState Update(Func<JobState, JobState> mutator)
    {
        JobState current, next;
        do
        {
            current = Volatile.Read(ref _state);
            next = mutator(current);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current));
        return next;
    }

    public bool TryTransition(JobStatus newStatus)
        => TryUpdate(s =>
        {
            if (!JobStateTransitions.IsValid(s.Status, newStatus))
            {
                return null;
            }

            var completedAt = newStatus is JobStatus.Completed or JobStatus.Failed
                ? DateTime.UtcNow
                : s.CompletedAt;
            return s with { Status = newStatus, CompletedAt = completedAt };
        });
}
