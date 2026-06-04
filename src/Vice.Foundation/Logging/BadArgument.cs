namespace Vice.Logging;

public sealed class BadArgument(string detail, Exception? inner = null) : ViceError(inner, detail)
{
    public string Detail { get; } = detail;
    public override ViceLogLevel LogLevel => ViceLogLevel.Debug;
    public override int ExitCode => Vice.Foundation.Execution.ViceExitCode.USAGE_ERROR;
    public override string ToString() => Detail;
}
