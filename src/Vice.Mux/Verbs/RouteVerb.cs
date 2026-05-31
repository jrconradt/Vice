using Vice.Composition;
using Vice.Lexicon;
using Vice.Mux.Strategies;

namespace Vice.Mux.Commands;

[ViceCommandPack]
public static class RouteCommands
{
    public static void Register(IViceApp app)
    {
        app.Register(
            Verbs.Route() > Connectors.By() * Targets.Strategy > Connectors.To() * Targets.Sinks,
            "Read stdin, route each chunk to the strategy-selected sink",
            (ctx, ct) => MuxRunner.RunAsync(ctx, ct, MuxStrategies.Registry, requireN: false));
    }
}
