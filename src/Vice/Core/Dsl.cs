using Vice.Nodes;
using Vice.Parser;

namespace Vice.Core;

public static class Dsl
{
    public static ChainNode verb(string name, params string[] synonyms)
    {
        var node = new WordNode(name);
        node.SynonymList.AddRange(synonyms);
        return node;
    }

    public static TargetDef target(string name, bool required = true, bool variadic = false)
        => new(name, required, variadic);

    public static ChainNode noun(string name) => new WordNode(name);

    public static ChainNode optional(ChainNode inner) => new OptionalNode(inner);
    public static ChainNode oneOf(params ChainNode[] alternatives) => new AlternationNode(alternatives);
    public static ChainNode repeat(ChainNode inner, int min = 0, int max = int.MaxValue, ChainNode? separator = null)
        => new RepetitionNode(inner, min, max, separator);
}
