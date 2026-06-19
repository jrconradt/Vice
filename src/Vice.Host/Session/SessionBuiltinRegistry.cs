using Vice.Contracts;

namespace Vice.Session;

internal sealed class SessionBuiltinRegistry
{
    private readonly Dictionary<string, Func<CommandContext, CancellationToken, Task<int>>> _handlers;
    private readonly InputHistory _history;

    public SessionBuiltinRegistry(InputHistory history)
    {
        _history = history;

        _handlers = new Dictionary<string, Func<CommandContext, CancellationToken, Task<int>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["exit"] = (_, _) => Task.FromResult(SessionLoop.EXIT_SENTINEL),
            ["quit"] = (_, _) => Task.FromResult(SessionLoop.EXIT_SENTINEL),
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

    private Task<int> HandleHistory(CommandContext ctx, CancellationToken ct)
    {
        var entries = _history.GetHistory();
        if (entries.Count == 0)
        {
            ctx.Console.WriteLine("No history.");
            return Task.FromResult(0);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            ctx.Console.WriteLine($"  {i + 1}  {entries[i]}");
        }

        return Task.FromResult(0);
    }

    private Task<int> HandleClear(CommandContext ctx, CancellationToken ct)
    {
        ctx.Console.Write("\x1b[2J\x1b[H");
        return Task.FromResult(0);
    }
}
