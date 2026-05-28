using Vice.Lexicon;
using Vice.Mux.Strategies;

namespace Vice.Mux.Commands;

internal static class SplitVerb
{
    public static void Register(IViceApp app, StrategyRegistry strategies)
    {
        app.Register(
            Verbs.Split() > Connectors.Into() * Targets.N > Connectors.By() * Targets.Strategy > Connectors.To() * Targets.Sinks,
            "Read stdin, route each chunk to one of {n} sinks selected by {strategy}",
            (ctx, ct) => MuxRunner.RunAsync(ctx, ct, strategies, requireN: true));
    }
}
