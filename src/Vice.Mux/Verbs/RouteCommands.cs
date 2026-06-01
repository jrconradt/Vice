using Vice.Composition;
using Vice.Contracts;
using Vice.Execution;
using Vice.Lexicon;
using Vice.Mux.Routing;
using static Vice.Dsl;

namespace Vice.Mux.Commands;

[ViceCommandPack]
public static class RouteCommands
{
    private const int MaxClauses = 64;

    public static void Register(IViceApp app)
    {
        app.Register(
            Verbs.Route()
                > repeat(Connectors.On() * target("cond") > Connectors.To() * target("sink"),
                         min: 1,
                         max: MaxClauses),
            "Route stdin to the destinations whose condition matches the upstream exit code",
            HandleAsync);
    }

    private static Task<int> HandleAsync(CommandContext ctx, CancellationToken ct)
    {
        var conds = ctx.GetTargets("cond");
        var sinks = ctx.GetTargets("sink");
        if (conds.Count == 0
            || conds.Count != sinks.Count)
        {
            throw new ArgumentException("route: each clause is `on <condition> to <destination>`");
        }

        var clauses = new List<RouteClause>(conds.Count);
        for (int i = 0; i < conds.Count; i++)
        {
            clauses.Add(new RouteClause(Condition.Parse(conds[i]), sinks[i]));
        }

        var code = ParseCode(ctx.GetGlobalOption("code"));
        var chunkSize = ctx.GetGlobalOption("chunk-size").AsPositiveInt() ?? MuxDefaults.DefaultChunkSize;
        return Router.RouteAsync(code,
                                 clauses,
                                 Console.OpenStandardInput(),
                                 chunkSize,
                                 ct);
    }

    private static int ParseCode(string? v)
        => (v is not null && int.TryParse(v, out var n)) ? n : 0;
}
