namespace Vice.Logging;

public sealed class CommandCompleted(string command, int exitCode, TimeSpan duration)
    : ViceError(null, command, exitCode, duration)
{
    public string Command { get; } = command;
    public int ExitCodeValue { get; } = exitCode;
    public TimeSpan Duration { get; } = duration;
    public override ViceLogLevel LogLevel => ViceLogLevel.Info;
    public override string ToString() => $"command completed: {Command} exit={ExitCodeValue} duration={Duration.TotalMilliseconds:0.0}ms";
}
