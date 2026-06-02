namespace Vice.Logging;

public sealed class Unhandled(Exception inner) : ViceError(inner, inner.Message)
{
    public override ViceLogLevel LogLevel => ViceLogLevel.Error;
    public override string? Hint => "Set VICE_LOG_LEVEL=debug to see the stack trace.";
    public override string ToString() => $"Error: {InnerException!.Message}";
}
