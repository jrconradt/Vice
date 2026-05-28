using Vice.Jobs;
using Vice.Logging;

namespace Vice.Session;

public sealed class SessionContext
{
    private readonly IReadOnlyDictionary<Type, object> _services;

    public bool IsInteractive { get; }
    public IJobSubmitter Jobs { get; }
    public SessionState State { get; }
    public IViceLogger Logger { get; }

    internal JobManager? JobManagerImpl { get; }

    internal SessionContext(
        JobManager jobManager,
        SessionState state,
        IReadOnlyDictionary<Type, object>? services = null,
        IViceLogger? logger = null,
        bool isInteractive = true)
    {
        Jobs = jobManager;
        JobManagerImpl = jobManager;
        State = state;
        IsInteractive = isInteractive;
        Logger = logger ?? NullViceLogger.Instance;
        _services = services ?? new Dictionary<Type, object>();
    }

    private SessionContext(
        IJobSubmitter jobs,
        SessionState state,
        IReadOnlyDictionary<Type, object>? services,
        IViceLogger? logger)
    {
        Jobs = jobs;
        JobManagerImpl = null;
        State = state;
        IsInteractive = false;
        Logger = logger ?? NullViceLogger.Instance;
        _services = services ?? new Dictionary<Type, object>();
    }

    internal static SessionContext OneShot(
        SessionState state,
        IReadOnlyDictionary<Type, object>? services,
        IViceLogger logger)
        => new(NoJobsSubmitter.Instance, state, services, logger);

    public T? GetService<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var svc))
        {
            return (T)svc;
        }

        Logger.Log(ViceLogLevel.Debug, $"unresolved service request: {typeof(T).FullName}");
        return null;
    }

    private sealed class NoJobsSubmitter : IJobSubmitter
    {
        public static readonly NoJobsSubmitter Instance = new();
        private NoJobsSubmitter() { }

        public Task<int> SubmitAsync(JobDescriptor descriptor, CancellationToken ct)
            => throw new InvalidOperationException(
                "Job submission requires an interactive session. " +
                "Run 'vice' with no args to enter the REPL.");
    }
}
