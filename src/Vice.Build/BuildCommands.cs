using Vice.Build.Dotnet;
using Vice.Composition;
using Vice.Contracts;
using Vice.Core;
using Vice.Lexicon;
using static Vice.Core.Dsl;

namespace Vice.Build;

[ViceCommandPack]
public static class BuildCommands
{
    public static void Register(IViceApp app)
    {
        app.Register(
            Verbs.Build() * Targets.PathOptional,
            "Build a project, solution, or directory via dotnet build",
            (ctx, ct) => Dispatch("build", ctx, ct));

        app.Register(
            Verbs.Test() * Targets.PathOptional,
            "Run tests for a project, solution, or directory via dotnet test",
            (ctx, ct) => Dispatch("test", ctx, ct));

        app.Register(
            Verbs.Restore() * Targets.PathOptional,
            "Restore dependencies for a project, solution, or directory via dotnet restore",
            (ctx, ct) => Dispatch("restore", ctx, ct));

        app.Register(
            Verbs.Clean() * Targets.PathOptional,
            "Clean build outputs for a project, solution, or directory via dotnet clean",
            (ctx, ct) => Dispatch("clean", ctx, ct));
    }

    private static Task<int> Dispatch(
        string verb,
        Vice.Contracts.CommandContext ctx,
        CancellationToken ct)
    {
        var canonical = CanonicalPath(ctx["path"]);
        var verbose = ctx.Verbose;
        var console = ctx.Console;
        return DotnetRunner.RunAsync("dotnet", verbose, console, ct, verb, canonical);
    }

    internal static string CanonicalPath(string? requested)
    {
        return Path.GetFullPath(requested ?? Directory.GetCurrentDirectory());
    }
}
