using System.Reflection;
using Vice;
using Vice.Build.Dotnet;
using Vice.Execution;
using Vice.Logging;
using Vice.Cli;
using Vice.Network.gRPC;

var logLevel = ParseLogLevel(Environment.GetEnvironmentVariable("VICE_LOG_LEVEL"));
IViceLogger logger = new ConsoleViceLogger(logLevel);

var connections = new GrpcConnectionManager(logger);
await using var buildQueue = new DotnetBuildQueue(logger);

using var cts = Vice.Signals.HookGracefulShutdown();

var host = new ViceHostServices
{
    Grpc = connections,
    Build = buildQueue,
};

await using var app = ViceApp.Create("vice", ResolveVersion())
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
            Console.Error.WriteLine($"vice: unknown VICE_LOG_LEVEL '{raw.Trim()}', defaulting to warn.");
            return ViceLogLevel.Warn;
        }
    }
}

static string ResolveVersion()
{
    var informational = Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;
    if (string.IsNullOrWhiteSpace(informational))
    {
        return "0.0.0";
    }

    var plus = informational.IndexOf('+');
    if (plus >= 0)
    {
        return informational[..plus];
    }

    return informational;
}
