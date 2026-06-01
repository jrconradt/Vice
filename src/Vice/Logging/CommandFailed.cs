namespace Vice.Logging;

public sealed class CommandFailed(string command, Exception cause, TimeSpan duration)
    : ViceError(cause, command, duration)
{
    public string Command { get; } = command;
    public TimeSpan Duration { get; } = duration;
    public string? InvocationId { get; } = InvocationScope.Current;
    public override ViceLogLevel LogLevel => ViceLogLevel.Error;
    public override string ToString() =>
        InvocationId is null
            ? $"command failed: {Command} duration={Duration.TotalMilliseconds:0.0}ms: {InnerException!.Message}"
            : $"command failed: {Command} duration={Duration.TotalMilliseconds:0.0}ms invocation={InvocationId}: {InnerException!.Message}";
}
