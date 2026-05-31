using Vice.Composition;
using Vice.Lexicon;
using Vice.Mux.Strategies;

namespace Vice.Mux.Commands;

[ViceCommandPack]
public static class StrategiesCommands
{
    public static void Register(IViceApp app)
    {
        app.Register(
            Verbs.Strategies(),
            "List the registered routing strategies",
            (ctx, ct) =>
            {
                foreach (var e in MuxStrategies.Registry.All)
                {
                    Vice.Output.Line($"{e.Name,-14} {e.Kind,-9}  {e.Description}");
                }

                return Task.FromResult(0);
            });
    }
}
