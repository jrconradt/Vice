using Vice;
using Vice.Execution;
using Vice.Mux;
using Vice.Mux.Commands;
using Vice.Mux.Strategies;
using Vice.Options;

var registry = StrategyRegistry.Default();

var builder = ViceApp.Create("vice-mux", "1.0.0")
    .WithDescription("Stream inspection and multiplexing for Vice pipelines")
    .WithGlobalOption(new ChunkSizeOption(),
                      new PeekOption(),
                      new SeedOption(),
                      new KeyOffsetOption(),
                      new KeyLengthOption(),
                      new StrategyArgOption());

await using var app = builder.Build();

InspectVerb.Register(app);
SplitVerb.Register(app, registry);
RouteVerb.Register(app, registry);
TeeVerb.Register(app);
StrategiesVerb.Register(app, registry);

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
