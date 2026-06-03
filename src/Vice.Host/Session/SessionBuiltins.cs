using Vice.Commands;
using Vice.Jobs;
using Vice.Lexicon;
using Vice.Logging;
using static Vice.Core.Dsl;

namespace Vice.Session;

internal static class SessionBuiltins
{
    internal static void RegisterChains(CommandRegistry registry)
    {
        registry.Register(
            Verbs.Exit(),
            "Exit the session",
            (ctx, ct) => Task.FromResult(SessionLoop.EXIT_SENTINEL),
            isBuiltin: true,
            showInHelp: true);

        registry.Register(
            Verbs.Jobs(),
            "List background jobs",
            (ctx, ct) => Task.FromResult(0),
            isBuiltin: true,
            showInHelp: true);

        registry.Register(
            Verbs.Pause() * Targets.Id,
            "Pause a background job",
            (ctx, ct) => Task.FromResult(0),
            isBuiltin: true,
            showInHelp: true);

        registry.Register(
            Verbs.Resume() * Targets.Id,
            "Resume a paused job",
            (ctx, ct) => Task.FromResult(0),
            isBuiltin: true,
            showInHelp: true);

        registry.Register(
            Verbs.Cancel() * Targets.Id,
            "Cancel a background job",
            (ctx, ct) => Task.FromResult(0),
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
