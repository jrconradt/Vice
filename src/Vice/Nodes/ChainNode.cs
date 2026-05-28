using Vice.Parser;

namespace Vice.Nodes;

public abstract class ChainNode : IChainDescriptor
{
    public abstract string Name { get; }
    public abstract ChainNodeKind Kind { get; }
    public List<string> SynonymList { get; } = new();
    public List<TargetDef> TargetList { get; } = new();
    public ChainNode? NextNode { get; set; }

    IReadOnlyList<string> IChainDescriptor.Synonyms => SynonymList;
    IReadOnlyList<ITargetDescriptor> IChainDescriptor.Targets => TargetList;
    IChainDescriptor? IChainDescriptor.Next => NextNode;
    ConjunctiveKind? IChainDescriptor.ConjunctiveKind => this is ConjunctiveNode cn ? cn.ConjunctiveKind : null;

    public abstract ChainNode Clone();

    protected void CopyTo(ChainNode target)
    {
        target.SynonymList.AddRange(SynonymList);
        target.TargetList.AddRange(TargetList);
        if (NextNode is not null)
        {
            target.NextNode = NextNode.Clone();
        }
    }

    public static ChainNode operator >(ChainNode left, ChainNode right)
    {

        var rightClone = right.Clone();

        var tail = left;
        while (tail.NextNode is not null)
        {
            tail = tail.NextNode;
        }

        tail.NextNode = rightClone;
        return left;
    }

    public static ChainNode operator <(ChainNode left, ChainNode right)
        => throw new NotSupportedException("Use > operator for chaining.");

    public static ChainNode operator |(ChainNode left, ChainNode right)
    {
        if (right is WordNode rightWord)
        {
            if (left is WordNode leftWord)
            {
                leftWord.SynonymList.Add(rightWord.Name);
                leftWord.SynonymList.AddRange(rightWord.SynonymList);

                if (rightWord.NextNode is not null && leftWord.NextNode is null)
                {
                    leftWord.NextNode = rightWord.NextNode;
                }
            }
            return left;
        }

        return left;
    }

    public static ChainNode operator |(ChainNode left, string right)
    {
        return left | (ChainNode)new WordNode(right);
    }
}
