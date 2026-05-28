using Vice.Parser;

namespace Vice.Nodes;

public sealed class AlternationNode : ChainNode, IChainDescriptor
{
    public IReadOnlyList<ChainNode> Alternatives { get; }

    public AlternationNode(params ChainNode[] alternatives)
    {
        if (alternatives.Length < 2)
        {
            throw new ArgumentException("Alternation requires at least two alternatives.", nameof(alternatives));
        }

        Alternatives = alternatives;
    }

    public override string Name => string.Join("|", Alternatives.Select(a => a.Name));
    public override ChainNodeKind Kind => ChainNodeKind.Alternation;

    public override ChainNode Clone()
    {
        var clone = new AlternationNode(Alternatives.Select(a => a.Clone()).ToArray());
        CopyTo(clone);
        return clone;
    }

    IReadOnlyList<IChainDescriptor> IChainDescriptor.Alternatives => Alternatives;
}
