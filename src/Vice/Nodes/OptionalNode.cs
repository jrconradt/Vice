using Vice.Parser;

namespace Vice.Nodes;

public sealed class OptionalNode : ChainNode, IChainDescriptor
{
    public ChainNode Inner { get; }

    public OptionalNode(ChainNode inner)
    {
        Inner = inner;
    }

    public override string Name => Inner.Name;
    public override ChainNodeKind Kind => ChainNodeKind.Optional;

    public override ChainNode Clone()
    {
        var clone = new OptionalNode(Inner.Clone());
        CopyTo(clone);
        return clone;
    }

    IChainDescriptor? IChainDescriptor.OptionalInner => Inner;
}
