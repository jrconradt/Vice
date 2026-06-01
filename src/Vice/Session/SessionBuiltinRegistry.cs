using Vice.Execution;
using Vice.Jobs;
using Vice.Logging;

namespace Vice.Session;

internal sealed class SessionBuiltinRegistry
{
    private readonly Dictionary<string, Func<CommandContext, CancellationToken, Task<int>>> _handlers;
    private readonly JobManager _jobManager;
    private readonly InputHistory _history;

    public SessionBuiltinRegistry(
        JobManager jobManager,
        InputHistory history)
    {
        _jobManager = jobManager;
        _history = history;

        _handlers = new Dictionary<string, Func<CommandContext, CancellationToken, Task<int>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["exit"] = (_, _) => Task.FromResult(SessionLoop.EXIT_SENTINEL),
            ["quit"] = (_, _) => Task.FromResult(SessionLoop.EXIT_SENTINEL),
            ["jobs"] = HandleJobs,
            ["pause"] = HandlePause,
            ["resume"] = HandleResume,
            ["cancel"] = HandleCancel,
            ["history"] = HandleHistory,
            ["clear"] = HandleClear,
        };
    }

    public bool TryGetHandler(IReadOnlyList<string> chain, out Func<CommandContext, CancellationToken, Task<int>>? handler)
    {
        handler = null;
        if (chain.Count == 0)
        {
            return false;
        }

        if (_handlers.TryGetValue(chain[0], out var h))
        {
            handler = h;
            return true;
        }
        return false;
    }

    private Task<int> HandleJobs(CommandContext ctx, CancellationToken ct)
    {
        var jobs = _jobManager.GetJobs();
        if (jobs.Count == 0)
        {
            Vice.Output.Line("No jobs.");
            return Task.FromResult(0);
        }

        foreach (var job in jobs)
        {
            var view = JobView.From(job);
            Vice.Output.Line($"  #{view.Id}  {view.Kind,-10} {view.Label,-30} {view.Status,-10} {view.Progress}");
        }
        return Task.FromResult(0);
    }

    private async Task<int> HandlePause(CommandContext ctx, CancellationToken ct)
    {
        if (!int.TryParse(ctx["id"], out var id))
        {
            throw new BadArgument("Invalid job ID.");
        }
        await _jobManager.PauseAsync(id, ct).ConfigureAwait(false);
        Vice.Output.Line($"Job #{id} paused.");
        return 0;
    }

    private async Task<int> HandleResume(CommandContext ctx, CancellationToken ct)
    {
        if (!int.TryParse(ctx["id"], out var id))
        {
            throw new BadArgument("Invalid job ID.");
        }
        await _jobManager.ResumeAsync(id, ct).ConfigureAwait(false);
        Vice.Output.Line($"Job #{id} resumed.");
        return 0;
    }

    private async Task<int> HandleCancel(CommandContext ctx, CancellationToken ct)
    {
        if (!int.TryParse(ctx["id"], out var id))
        {
            throw new BadArgument("Invalid job ID.");
        }
        await _jobManager.CancelAsync(id, ct).ConfigureAwait(false);
        Vice.Output.Line($"Job #{id} cancelled.");
        return 0;
    }

    private Task<int> HandleHistory(CommandContext ctx, CancellationToken ct)
    {
        var entries = _history.GetHistory();
        if (entries.Count == 0)
        {
            Vice.Output.Line("No history.");
            return Task.FromResult(0);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            Vice.Output.Line($"  {i + 1}  {entries[i]}");
        }

        return Task.FromResult(0);
    }

    private Task<int> HandleClear(CommandContext ctx, CancellationToken ct)
    {
        Vice.Output.Write("\x1b[2J\x1b[H");
        return Task.FromResult(0);
    }
}
