using Vice.Build.Dotnet;
using Vice.Composition;
using Vice.Contracts;
using Vice.Execution;

namespace Vice.Build;

[ViceCommand("project", Verb = "outdated", Description = "List outdated NuGet packages for a project or solution via dotnet list package --outdated")]
public sealed partial class OutdatedCommand : IViceCommand
{
    public Task<int> Handle(CommandContext ctx, CancellationToken ct)
    {
        var canonical = BuildCommands.CanonicalPath(ctx["project"]);
        return DotnetRunner.RunAsync(
            "dotnet",
            ctx.Verbose,
            ctx.Console,
            ct,
            "list",
            canonical,
            "package",
            "--outdated");
    }
}
