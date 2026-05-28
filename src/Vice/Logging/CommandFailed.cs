namespace Vice.Logging;

public sealed class CommandFailed(string command, Exception cause, TimeSpan duration)
    : ViceError(cause, command, duration)
{
    public string Command { get; } = command;
    public TimeSpan Duration { get; } = duration;
    public override ViceLogLevel LogLevel => ViceLogLevel.Error;
    public override string ToString() => $"command failed: {Command} duration={Duration.TotalMilliseconds:0.0}ms: {InnerException!.Message}";
}
