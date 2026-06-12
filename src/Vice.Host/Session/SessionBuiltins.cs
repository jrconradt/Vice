using System.Diagnostics;
using Vice.Commands;
using Vice.Foundation.Execution;
using Vice.Jobs;
using Vice.Lexicon;
using Vice.Logging;
using static Vice.Core.Dsl;

namespace Vice.Session;

internal static class SessionBuiltins
{
    internal static void RegisterChains(CommandRegistry registry,
                                        string appName,
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
            Verbs.Jobs(),
            "List background jobs",
            async (ctx, ct) =>
            {
                var records = await JobLedger.ReadAllAsync(JobLedger.RootFor(appName), logger, ct).ConfigureAwait(false);
                if (records.Count == 0)
                {
                    ctx.Console.WriteLine("No jobs.");
                    return 0;
                }

                foreach (var job in records)
                {
                    ctx.Console.WriteLine($"  #{job.Id}  {job.Kind.Name,-10} {JobLabel(job),-30} {job.Status,-10} {JobProgressText(job)}");
                }

                return 0;
            },
            isBuiltin: true,
            showInHelp: true);

        registry.Register(
            Verbs.Cancel() * Targets.Id,
            "Cancel a background job",
            async (ctx, ct) =>
            {
                if (!int.TryParse(ctx["id"], out var id))
                {
                    throw new BadArgument("Invalid job ID.");
                }

                var root = JobLedger.RootFor(appName);
                var record = await JobLedger.ReadAsync(root, id, logger, ct).ConfigureAwait(false);
                if (record is null)
                {
                    ctx.Console.WriteError($"No such job: #{id}.");
                    return ViceExitCode.FAILURE;
                }

                if (record.Status != JobStatus.Running)
                {
                    ctx.Console.WriteLine($"Job #{id} is not running.");
                    return ViceExitCode.SUCCESS;
                }

                if (JobLedger.IsProcessAlive(id, record.ProcessStartTimeUtc))
                {
                    try
                    {
                        using var process = Process.GetProcessById(id);
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                    {
                        logger.Log(ViceLogLevel.Debug, $"job {id} exited before it could be killed", ex);
                    }
                }

                await JobLedger.MarkCancelledAsync(root, record, ct).ConfigureAwait(false);
                ctx.Console.WriteLine($"Job #{id} cancelled.");
                return ViceExitCode.SUCCESS;
            },
            isBuiltin: true,
            showInHelp: true);

        registry.Register(
            Verbs.Job() > noun("run") * target("descriptor"),
            "Run a JSON job descriptor in the foreground; background submissions spawn this command detached.",
            async (ctx, ct) => await JobHarness.RunAsync(jobRunners,
                                                         ctx.Require("descriptor"),
                                                         appName,
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

    private static string JobLabel(JobState job)
    {
        return string.IsNullOrEmpty(job.Label) ? job.Kind.Name : job.Label;
    }

    private static string JobProgressText(JobState job)
    {
        if (job.ProgressTotal is { } total
            && total > 0)
        {
            return $"{job.ProgressCurrent * 100 / total}%";
        }

        if (job.ProgressCurrent > 0)
        {
            return $"{job.ProgressCurrent}";
        }

        return "";
    }
}
