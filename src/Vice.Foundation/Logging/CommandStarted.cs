namespace Vice.Logging;

public sealed class CommandStarted(string command) : ViceError(null, command)
{
    public string Command { get; } = command;
    public string? InvocationId { get; } = InvocationScope.Current;
    public override ViceLogLevel LogLevel => ViceLogLevel.Info;
    public override string ToString() =>
        InvocationId is null
            ? $"command started: {Command}"
            : $"command started: {Command} invocation={InvocationId}";
}
