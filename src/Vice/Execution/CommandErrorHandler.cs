using Vice.Logging;
using Vice.Streaming;

namespace Vice.Execution;

public static class CommandErrorHandler
{
    public static int Handle(CommandContext ctx, ViceError error)
    {
        ctx.Logger.Log(error);
        ctx.Console.WriteError(error.ToString());
        if (error.Hint is { } hint)
        {
            ctx.Console.WriteError($"hint: {hint}");
        }

        return error.ExitCode;
    }

    public static int HandleStreamingError<T>(IStreamingCommandContext<T> ctx, ViceError error)
    {
        var logger = ctx.Session?.GetService<IViceLogger>() ?? NullViceLogger.Instance;
        logger.Log(error);
        ctx.Stream.Fault(error);
        ctx.Console.WriteError(error.ToString());
        if (error.Hint is { } hint)
        {
            ctx.Console.WriteError($"hint: {hint}");
        }

        return error.ExitCode;
    }
}
