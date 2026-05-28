using Vice.Display;

namespace Vice;

public static class Signals
{
    public static CancellationTokenSource HookGracefulShutdown(IConsoleWriter? console = null)
    {
        var cts = new CancellationTokenSource();
        var writer = console ?? new ConsoleWriter();
        int count = 0;
        Console.CancelKeyPress += (_, e) =>
        {
            var n = Interlocked.Increment(ref count);
            if (n == 1)
            {
                e.Cancel = true;
                cts.Cancel();
                writer.WriteError("Shutting down — press Ctrl+C again to force exit.");
            }
        };
        return cts;
    }

    public static bool IsBrokenPipe(Exception ex)
    {
        if (ex is IOException io && io.InnerException is { } inner && inner.GetType().Name == "SocketException")
        {
            return true;
        }

        if (ex is IOException && (ex.Message.Contains("Broken pipe", StringComparison.OrdinalIgnoreCase)
                               || ex.Message.Contains("Pipe is broken", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }
}
