using Vice.Foundation.Execution;

namespace Vice.Execution;

public readonly record struct CommandResult(int ExitCode)
{
    public static readonly CommandResult Success = new(ViceExitCode.SUCCESS);
    public static readonly CommandResult Failure = new(ViceExitCode.FAILURE);
    public static readonly CommandResult UsageError = new(ViceExitCode.USAGE_ERROR);
    public static readonly CommandResult Interrupted = new(ViceExitCode.INTERRUPTED);
}
