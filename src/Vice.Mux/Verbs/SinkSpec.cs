using Vice.Contracts;
using Vice.Execution;

namespace Vice.Mux.Commands;

internal static class SinkSpec
{
    public static List<string> Collect(CommandContext ctx, string name)
    {
        var list = new List<string>();
        var raw = ctx.GetTarget(name);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                list.Add(part);
            }
        }
        if (list.Count == 0)
        {
            var pipeline = ctx.PipelineInput;
            if (!string.IsNullOrWhiteSpace(pipeline))
            {
                list.AddRange(pipeline.Split(new[] { ' ', '\t', '\n', '\r', ',' },
                    StringSplitOptions.RemoveEmptyEntries));
            }
        }
        return list;
    }
}
