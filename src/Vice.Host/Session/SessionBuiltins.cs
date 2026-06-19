using Vice.Commands;
using Vice.Jobs;
using Vice.Lexicon;
using Vice.Logging;
using static Vice.Core.Dsl;

namespace Vice.Session;

internal static class SessionBuiltins
{
    internal static void RegisterChains(CommandRegistry registry,
                                        IReadOnlyList<IJobRunner> jobRunners,
                                        IViceLogger logger)
    {
        registry.Register(
            Verbs.Exit(),
            "Exit the session",
            (ctx, ct) => Task.FromResult(SessionLoop.EXIT_SENTINEL),
            isBuiltin: true,
            showInHelp: true);

        registry.Register(
            Verbs.Job() > noun("run") * target("descriptor"),
            "Run a JSON job descriptor in the foreground; background submissions spawn this command detached.",
            async (ctx, ct) => await JobHarness.RunAsync(jobRunners,
                                                         ctx.Require("descriptor"),
                                                         logger,
                                                         ct).ConfigureAwait(false),
            isBuiltin: true,
            showInHelp: true);

        registry.Register(
            Verbs.History(),
            "Show command history",
            (ctx, ct) => Task.FromResult(0),
            isBuiltin: true,
            showInHelp: true);

        registry.Register(
            Verbs.Clear(),
            "Clear the screen",
            (ctx, ct) =>
            {
                ctx.Console.Write("\x1b[2J\x1b[H");
                return Task.FromResult(0);
            },
            isBuiltin: true,
            showInHelp: true);
    }
}
