using System.Reflection;
using Vice.Build.Dotnet;
using Vice.Cli;
using Vice.Core;
using Vice.Foundation.Execution;
using Vice.Host;
using Vice.Logging;
using Vice.Mux.Sinks;
using Vice.Net.Requests.Grpc;

var daemonMode = args.Contains("--daemon");
var logLevel = LogLevelEnv.Resolve("vice");
var logSink = LogDestination.Resolve(daemonMode, "vice");
await using var logger = new ConsoleViceLogger(logLevel, logSink);

var connections = new GrpcConnectionManager(logger);
var buildQueue = new DotnetBuildQueue(logger);
var researchHttp = new Vice.Research.ResearchHttpService(logger);

using var cts = Vice.Core.Signals.HookGracefulShutdown();

var host = new ViceHostServices
{
    Grpc = connections,
    Build = buildQueue,
    ResearchHttp = researchHttp,
    ConnectTcpSink = SinkFactory.ConnectTcpAsync,
};

await using var app = ViceApp.Create("vice", ResolveVersion())
    .WithDescription("gRPC connection and streaming commands")
    .WithLogger(logger)
    .ComposeFromAttributes(host)
    .Build()
    .RegisterDiscoveredPacks(host);

try
{
    if (daemonMode)
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
catch (IOException ex) when (Vice.Core.Signals.IsBrokenPipe(ex))
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
