using Vice.Jobs;
using Vice.Logging;

namespace Vice.Contracts;

public interface ISessionContext
{
    bool IsInteractive { get; }
    IJobSubmitter Jobs { get; }
    SessionState State { get; }
    IViceLogger Logger { get; }

    T? GetService<T>() where T : class;
}
