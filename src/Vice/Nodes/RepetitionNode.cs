using Vice.Parser;

namespace Vice.Nodes;

public sealed class RepetitionNode : ChainNode, IChainDescriptor
{
    public ChainNode Inner { get; }
    public ChainNode? Separator { get; }
    public int Min { get; }
    public int Max { get; }

    public RepetitionNode(ChainNode inner, int min = 0, int max = int.MaxValue, ChainNode? separator = null)
    {
        if (min < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min));
        }

        if (max < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max));
        }

        Inner = inner;
        Separator = separator;
        Min = min;
        Max = max;
    }

    public override string Name => Inner.Name;
    public override ChainNodeKind Kind => ChainNodeKind.Repetition;

    public override ChainNode Clone()
    {
        var clone = new RepetitionNode(Inner.Clone(), Min, Max, Separator?.Clone());
        CopyTo(clone);
        return clone;
    }

    IChainDescriptor? IChainDescriptor.RepetitionInner => Inner;
    IChainDescriptor? IChainDescriptor.RepetitionSeparator => Separator;
    (int Min, int Max) IChainDescriptor.RepetitionBounds => (Min, Max);
}
