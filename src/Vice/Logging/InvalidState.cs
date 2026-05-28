namespace Vice.Logging;

public sealed class InvalidState(string detail, Exception? inner = null) : ViceError(inner, detail)
{
    public string Detail { get; } = detail;
    public override ViceLogLevel LogLevel => ViceLogLevel.Debug;
    public override string ToString() => Detail;
}
