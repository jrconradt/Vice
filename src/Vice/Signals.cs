using System.Runtime.InteropServices;
using Vice.Display;
using Vice.Display.Rendering;

namespace Vice;

public static class Signals
{
    public static CancellationTokenSource HookGracefulShutdown(IConsoleWriter? console = null)
    {
        var writer = console ?? new ConsoleWriter(Output.Current);
        var cts = new ShutdownTokenSource();
        int count = 0;
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            var n = Interlocked.Increment(ref count);
            if (n == 1)
            {
                e.Cancel = true;
                cts.Cancel();
                writer.WriteError("Shutting down — press Ctrl+C again to force exit.");
            }
        };
        cts.Handler = handler;
        Console.CancelKeyPress += handler;

        Action<PosixSignalContext> terminate = ctx =>
        {
            var n = Interlocked.Increment(ref count);
            if (n == 1)
            {
                ctx.Cancel = true;
                cts.Cancel();
                writer.WriteError("Shutting down.");
            }
        };
        cts.SigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, terminate);
        cts.SigQuit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, terminate);
        return cts;
    }

    private sealed class ShutdownTokenSource : CancellationTokenSource
    {
        public ConsoleCancelEventHandler? Handler { get; set; }

        public PosixSignalRegistration? SigTerm { get; set; }

        public PosixSignalRegistration? SigQuit { get; set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Handler is not null)
            {
                Console.CancelKeyPress -= Handler;
                Handler = null;
            }

            if (disposing)
            {
                SigTerm?.Dispose();
                SigTerm = null;
                SigQuit?.Dispose();
                SigQuit = null;
            }

            base.Dispose(disposing);
        }
    }

    public static bool IsBrokenPipe(Exception ex)
    {
        if (ex is IOException io && io.InnerException is { } inner
            && inner.GetType().Name == "SocketException")
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
