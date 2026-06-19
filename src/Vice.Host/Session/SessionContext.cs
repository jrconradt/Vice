using Vice.Contracts;
using Vice.Jobs;
using Vice.Logging;

namespace Vice.Session;

public sealed class SessionContext : ISessionContext
{
    private readonly IReadOnlyDictionary<Type, object> _services;

    public bool IsInteractive { get; }
    public IJobSubmitter Jobs { get; }
    public SessionState State { get; }
    public IViceLogger Logger { get; }

    internal SessionContext(
        IJobSubmitter jobs,
        SessionState state,
        IReadOnlyDictionary<Type, object>? services = null,
        IViceLogger? logger = null,
        bool isInteractive = true)
    {
        Jobs = jobs;
        State = state;
        IsInteractive = isInteractive;
        Logger = logger ?? NullViceLogger.Instance;
        _services = services ?? new Dictionary<Type, object>();
    }

    internal static SessionContext OneShot(
        IJobSubmitter jobs,
        SessionState state,
        IReadOnlyDictionary<Type, object>? services,
        IViceLogger logger)
        => new(jobs, state, services, logger, isInteractive: false);

    public T? GetService<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var svc))
        {
            return (T)svc;
        }

        Logger.Log(ViceLogLevel.Debug, $"unresolved service request: {typeof(T).FullName}");
        return null;
    }
}
