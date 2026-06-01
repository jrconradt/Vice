using Vice.Execution;

namespace Vice.Logging;

public sealed class CommandCompleted(string command, int exitCode, TimeSpan duration)
    : ViceError(null, command, exitCode, duration)
{
    public string Command { get; } = command;
    public int ExitCodeValue { get; } = exitCode;
    public TimeSpan Duration { get; } = duration;
    public string? InvocationId { get; } = CommandContext.CurrentInvocationId;
    public override ViceLogLevel LogLevel => ViceLogLevel.Info;
    public override string ToString() =>
        InvocationId is null
            ? $"command completed: {Command} exit={ExitCodeValue} duration={Duration.TotalMilliseconds:0.0}ms"
            : $"command completed: {Command} exit={ExitCodeValue} duration={Duration.TotalMilliseconds:0.0}ms invocation={InvocationId}";
}
