using Vice;
using Vice.Execution;
using Vice.Logging;
using Vice.Net;
using Vice.Network.gRPC;

var logLevel = ParseLogLevel(Environment.GetEnvironmentVariable("VICE_LOG_LEVEL"));
IViceLogger logger = new ConsoleViceLogger(logLevel);

var connections = new GrpcConnectionManager(logger);

using var cts = Vice.Signals.HookGracefulShutdown();

var host = new ViceHostServices
{
    Grpc = connections,
};

await using var app = ViceApp.Create("vice", "1.0.0")
    .WithDescription("gRPC connection and streaming commands")
    .WithLogger(logger)
    .ComposeFromAttributes(host)
    .Build()
    .RegisterDiscoveredPacks(host);

try
{
    if (args.Contains("--daemon"))
    {
        return await app.RunDaemonAsync(cts.Token);
    }

    var nonInteractive = args.Contains("--non-interactive");
    if (args.Length == 0 || (nonInteractive && args.All(a => a.StartsWith('-'))))
    {
        if (nonInteractive)
        {
            Console.Error.WriteLine("vice: --non-interactive refuses REPL launch with no command.");
            return ViceExitCode.USAGE_ERROR;
        }

        return await app.RunSessionAsync(cts.Token);
    }

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
catch (Exception ex)
{
    return RenderTopLevelError(ex, logger);
}

static int RenderTopLevelError(Exception ex, IViceLogger logger)
{
    var error = ex as ViceError ?? TranslateTopLevel(ex);
    logger.Log(error);
    Console.Error.WriteLine(error.ToString());
    if (error.Hint is { } hint)
    {
        Console.Error.WriteLine($"hint: {hint}");
    }

    return error.ExitCode;
}

static ViceError TranslateTopLevel(Exception ex) => ex switch
{
    FileNotFoundException fnf => new FileMissing(fnf.FileName ?? "<unknown>", fnf),
    DirectoryNotFoundException dnf => new FileMissing("<directory>", dnf),
    _ => new Unhandled(ex),
};

static ViceLogLevel ParseLogLevel(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return ViceLogLevel.Warn;
    }

    return raw.Trim().ToLowerInvariant() switch
    {
        "trace" => ViceLogLevel.Trace,
        "debug" => ViceLogLevel.Debug,
        "info" => ViceLogLevel.Info,
        "warn" or "warning" => ViceLogLevel.Warn,
        "error" => ViceLogLevel.Error,
        _ => ViceLogLevel.Warn,
    };
}
