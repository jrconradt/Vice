namespace Vice.Logging;

public sealed class TimedOut(Exception? inner = null) : ViceError(inner)
{
    public override ViceLogLevel LogLevel => ViceLogLevel.Debug;
    public override string? Hint => "Increase --timeout, or verify the destination is reachable.";
    public override string ToString() => "Operation timed out.";
}
