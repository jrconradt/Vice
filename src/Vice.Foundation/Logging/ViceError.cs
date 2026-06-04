namespace Vice.Logging;

public abstract class ViceError : Exception
{
    public abstract ViceLogLevel LogLevel { get; }
    public object?[] Params { get; }

    public virtual int ExitCode => Vice.Foundation.Execution.ViceExitCode.FAILURE;

    public virtual string? Hint => null;

    protected ViceError(Exception? inner, params object?[] arguments) : base("", inner)
    {
        Params = arguments;
    }
}
