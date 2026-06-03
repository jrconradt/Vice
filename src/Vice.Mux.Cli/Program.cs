using Vice.Core;
using Vice.Foundation.Execution;
using Vice.Host;
using Vice.Logging;
using Vice.Mux.Cli;

var logLevel = LogLevelEnv.Resolve("vice-mux");
await using var logger = new ConsoleViceLogger(logLevel);

var host = new ViceMuxHost();

await using var app = ViceApp.Create("vice-mux", "1.0.0")
    .WithDescription("Stream inspection and multiplexing for Vice pipelines")
    .WithLogger(logger)
    .ComposeFromAttributes(host)
    .Build()
    .RegisterDiscoveredPacks(host);

using var cts = Vice.Core.Signals.HookGracefulShutdown();

try
{
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
