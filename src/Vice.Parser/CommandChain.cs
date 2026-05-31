namespace Vice.Parser;

public sealed class CommandChain
{
    public IReadOnlyList<ResolvedCommand> Nodes { get; }

    public CommandChain(IReadOnlyList<ResolvedCommand> nodes)
    {
        Nodes = nodes;
    }

    public IReadOnlyDictionary<string, string> AllTargetValues()
    {
        var values = new Dictionary<string, string>();
        foreach (var node in Nodes)
        {
            foreach (var kv in node.TargetValues)
            {
                values[kv.Key] = kv.Value;
            }
        }
        return values;
    }
}
