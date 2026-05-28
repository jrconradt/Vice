using Vice.Lexicon;
using Vice.Mux.Strategies;

namespace Vice.Mux.Commands;

internal static class RouteVerb
{
    public static void Register(IViceApp app, StrategyRegistry strategies)
    {
        app.Register(
            Verbs.Route() > Connectors.By() * Targets.Strategy > Connectors.To() * Targets.Sinks,
            "Read stdin, route each chunk to the strategy-selected sink",
            (ctx, ct) => MuxRunner.RunAsync(ctx, ct, strategies, requireN: false));
    }
}
