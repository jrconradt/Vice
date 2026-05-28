namespace Vice.Logging;

public sealed class CommandStarted(string command) : ViceError(null, command)
{
    public string Command { get; } = command;
    public override ViceLogLevel LogLevel => ViceLogLevel.Info;
    public override string ToString() => $"command started: {Command}";
}
