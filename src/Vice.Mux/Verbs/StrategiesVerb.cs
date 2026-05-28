using Vice.Lexicon;
using Vice.Mux.Strategies;

namespace Vice.Mux.Commands;

internal static class StrategiesVerb
{
    public static void Register(IViceApp app, StrategyRegistry strategies)
    {
        app.Register(
            Verbs.Strategies(),
            "List the registered routing strategies",
            (ctx, ct) =>
            {
                foreach (var e in strategies.All)
                {
                    Vice.Output.Line($"{e.Name,-14} {e.Kind,-9}  {e.Description}");
                }

                return Task.FromResult(0);
            });
    }
}
