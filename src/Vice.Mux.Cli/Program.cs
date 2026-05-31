using Vice;
using Vice.Execution;
using Vice.Logging;
using Vice.Mux.Cli;

var logLevel = ParseLogLevel(Environment.GetEnvironmentVariable("VICE_LOG_LEVEL"));
IViceLogger logger = new ConsoleViceLogger(logLevel);

var host = new ViceMuxHost();

await using var app = ViceApp.Create("vice-mux", "1.0.0")
    .WithDescription("Stream inspection and multiplexing for Vice pipelines")
    .WithLogger(logger)
    .ComposeFromAttributes(host)
    .Build()
    .RegisterDiscoveredPacks(host);

using var cts = Vice.Signals.HookGracefulShutdown();

try
{
    return await app.RunAsync(args, cts.Token);
}
catch (OperationCanceledException)
{
    return ViceExitCode.INTERRUPTED;
}
catch (IOException ex) when (Vice.Signals.IsBrokenPipe(ex))
{
    return ViceExitCode.SUCCESS;
}

static ViceLogLevel ParseLogLevel(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return ViceLogLevel.Warn;
    }

    var normalized = raw.Trim().ToLowerInvariant();
    switch (normalized)
    {
        case "trace":
        {
            return ViceLogLevel.Trace;
        }
        case "debug":
        {
            return ViceLogLevel.Debug;
        }
        case "info":
        {
            return ViceLogLevel.Info;
        }
        case "warn":
        case "warning":
        {
            return ViceLogLevel.Warn;
        }
        case "error":
        {
            return ViceLogLevel.Error;
        }
        default:
        {
            Console.Error.WriteLine($"vice-mux: unknown VICE_LOG_LEVEL '{raw.Trim()}', defaulting to warn.");
            return ViceLogLevel.Warn;
        }
    }
}
