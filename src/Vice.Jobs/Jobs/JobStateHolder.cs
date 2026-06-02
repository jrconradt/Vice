namespace Vice.Jobs;

internal sealed class JobStateHolder
{
    private JobState _state;

    public JobStateHolder(JobState initial)
    {
        _state = initial;
    }

    public JobState Read() => Volatile.Read(ref _state);

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
