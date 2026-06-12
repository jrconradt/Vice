using Vice.Commands;
using Vice.Completions;
using Vice.Contracts;
using Vice.Foundation.Execution;
using Vice.Help;
using Vice.Ipc;
using Vice.Lexicon;
using Vice.Manpages;
using Vice.Options;
using static Vice.Core.Dsl;

namespace Vice.Host.Commands;

internal static class BuiltinCommands
{
    public static void Register(CommandRegistry registry, ViceApp app)
    {

        registry.Register(
            Verbs.Help() * Targets.Command,
            "Show help",
            async (ctx, ct) =>
            {
                var commandName = ctx.GetTarget("command");
                if (commandName is null)
                {
                    HelpBuilder.WriteHelp(
                        BuildHelpTitle(app.Name, app.Version, app.Description),
                        registry.HelpVisibleRegistrations,
                        OrderGlobalOptions(app.RegisteredGlobalOptions, StringComparer.OrdinalIgnoreCase),
                        ctx.Render);
                }
                else
                {
                    var registration = registry.FindByVerb(commandName);
                    if (registration is null)
                    {
                        var matches = registry.FindContaining(commandName);
                        if (matches.Count == 1)
                        {
                            HelpBuilder.WriteCommandHelp(matches[0], ctx.Render);
                            return 0;
                        }
                        if (matches.Count > 1)
                        {
                            ctx.Console.WriteLine($"'{commandName}' appears in multiple commands:");
                            foreach (var m in matches)
                            {
                                ctx.Console.WriteLine($"  {HelpFormatter.FormatChain(m.Chain)}");
                            }

                            return 0;
                        }
                        ctx.Console.WriteError($"Unknown command: '{commandName}'.");
                        return ViceExitCode.USAGE_ERROR;
                    }

                    HelpBuilder.WriteCommandHelp(registration, ctx.Render);
                }

                return 0;
            },
            isBuiltin: true);

        registry.Register(
            Verbs.Version(),
            "Show version",
            async (ctx, ct) =>
            {
                ctx.Console.WriteLine($"{app.Name} v{app.Version}");
                return 0;
            },
            isBuiltin: true);

        registry.Register(
            Verbs.List() > Nouns.Commands(),
            "List all commands",
            async (ctx, ct) =>
            {
                foreach (var reg in registry.UserRegistrations)
                {
                    ctx.Console.WriteLine(HelpFormatter.FormatChain(reg.Chain));
                }

                return 0;
            },
            isBuiltin: true);

        registry.Register(
            Verbs.Daemon(),
            "Run as a background daemon serving the IPC control channel.",
            async (ctx, ct) => await app.RunDaemonAsync(ct).ConfigureAwait(false),
            isBuiltin: true);

        registry.Register(
            Verbs.Status(),
            "Query a running daemon for job status (if any).",
            async (ctx, ct) =>
            {
                var state = SessionState.For(app.Name);
                PipeClient? client;
                try
                {
                    client = await PipeClient.TryConnectAsync(state.PipeName, timeoutMs: 500, app.Logger, ct).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException ex)
                {
                    ctx.Console.WriteError($"Permission denied connecting to daemon pipe '{state.PipeName}': {ex.Message}");
                    return ViceExitCode.FAILURE;
                }
                if (client is null)
                {
                    ctx.Console.WriteLine($"No {app.Name} daemon running.");
                    return ViceExitCode.SUCCESS;
                }
                await using (client)
                {
                    var healthResponse = await client.SendAsync(new HealthRequest(), ct).ConfigureAwait(false);
                    if (healthResponse is not HealthResponse health)
                    {
                        ctx.Console.WriteError("Daemon returned unexpected response.");
                        return ViceExitCode.FAILURE;
                    }

                    var healthy = health.Listening && !health.AcceptLoopCrashed;
                    ctx.Console.WriteLine($"{app.Name} daemon v{health.Version} — {(healthy ? "healthy" : "unhealthy")}");
                    ctx.Console.WriteLine($"  listening: {(health.Listening ? "yes" : "no")}");
                    ctx.Console.WriteLine($"  accept loop: {(health.AcceptLoopCrashed ? $"crashed ({health.FaultSummary ?? "unknown fault"})" : "running")}");
                    ctx.Console.WriteLine($"  uptime: {health.UptimeSeconds:0.0}s");
                    ctx.Console.WriteLine($"  jobs: {health.JobCount}");

                    var jobsResponse = await client.SendAsync(new JobStatusRequest(), ct).ConfigureAwait(false);
                    if (jobsResponse is not JobStatusResponse statuses)
                    {
                        ctx.Console.WriteError("Daemon returned unexpected response.");
                        return ViceExitCode.FAILURE;
                    }
                    if (statuses.Jobs.Count == 0)
                    {
                        ctx.Console.WriteLine("No jobs.");
                        return healthy ? ViceExitCode.SUCCESS : ViceExitCode.FAILURE;
                    }
                    foreach (var job in statuses.Jobs)
                    {
                        ctx.Console.WriteLine($"#{job.Id} [{job.Kind}] {job.Status} — {job.Label}");
                    }

                    return healthy ? ViceExitCode.SUCCESS : ViceExitCode.FAILURE;
                }
            },
            isBuiltin: true);

        registry.Register(
            Verbs.Manpage(),
            "Emit a groff(7) man page for this tool to stdout.",
            async (ctx, ct) =>
            {
                var page = ManPageGenerator.Generate(
                    app.Name, app.Version,
                    app.Description ?? "command-line interface",
                    app.Description,
                    registry.HelpVisibleRegistrations,
                    OrderGlobalOptions(app.RegisteredGlobalOptions, StringComparer.Ordinal));
                ctx.Console.WriteLine(page);
                return 0;
            },
            isBuiltin: true);

        registry.Register(
            Verbs.Completions() * Targets.Shell,
            "Emit a shell completion script (bash | zsh) to stdout.",
            async (ctx, ct) =>
            {
                var shell = ctx.GetTarget("shell")?.Trim().ToLowerInvariant();
                var model = CompletionModelBuilder.Build(
                    app.Name, registry.Registrations, app.RegisteredGlobalOptions);
                var script = shell switch
                {
                    "bash" => BashCompletionGenerator.Generate(model),
                    "zsh" => ZshCompletionGenerator.Generate(model),
                    _ => null
                };
                if (script is null)
                {
                    ctx.Console.WriteError($"Unsupported shell '{shell}'. Supported: bash, zsh.");
                    return ViceExitCode.USAGE_ERROR;
                }
                ctx.Console.WriteLine(script);
                return 0;
            },
            isBuiltin: true);
    }

    internal static string? BuildHelpTitle(string appName, string version, string? description)
    {
        if (string.IsNullOrEmpty(appName))
        {
            return null;
        }

        return description is not null
            ? $"{appName} v{version} - {description}"
            : $"{appName} v{version}";
    }

    internal static IReadOnlyList<GlobalOption> OrderGlobalOptions(
        IEnumerable<GlobalOption> options,
        StringComparer comparer)
        => options.OrderBy(o => o.Name, comparer).ToList();
}
