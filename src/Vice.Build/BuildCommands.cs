using Vice.Build.Dotnet;
using Vice.Composition;
using Vice.Contracts;
using Vice.Lexicon;
using static Vice.Dsl;

namespace Vice.Build;

[ViceCommandPack]
public static class BuildCommands
{
    public static void Register(IViceApp app, DotnetBuildQueue queue)
    {
        app.Register(
            Verbs.Build() * Targets.PathOptional,
            "Build a project, solution, or directory via dotnet build",
            (ctx, ct) => Dispatch(queue, "build", ctx, ct));

        app.Register(
            Verbs.Test() * Targets.PathOptional,
            "Run tests for a project, solution, or directory via dotnet test",
            (ctx, ct) => Dispatch(queue, "test", ctx, ct));

        app.Register(
            Verbs.Restore() * Targets.PathOptional,
            "Restore dependencies for a project, solution, or directory via dotnet restore",
            (ctx, ct) => Dispatch(queue, "restore", ctx, ct));

        app.Register(
            Verbs.Clean() * Targets.PathOptional,
            "Clean build outputs for a project, solution, or directory via dotnet clean",
            (ctx, ct) => Dispatch(queue, "clean", ctx, ct));
    }

    private static Task<int> Dispatch(
        DotnetBuildQueue queue,
        string verb,
        Vice.Contracts.CommandContext ctx,
        CancellationToken ct)
    {
        var canonical = CanonicalPath(ctx["path"]);
        var key = BuildKey(verb, canonical);
        var verbose = ctx.Verbose;
        return queue.GetOrStart(
            key,
            () => DotnetRunner.RunAsync("dotnet", verbose, ct, verb, canonical),
            ct);
    }

    internal static string CanonicalPath(string? requested)
    {
        return Path.GetFullPath(requested ?? Directory.GetCurrentDirectory());
    }

    internal static string BuildKey(string verb, string canonical)
    {
        return $"{verb}::{canonical}";
    }
}
