using Vice.Parser;

namespace Vice.Nodes;

public abstract class ChainNode : IChainDescriptor
{
    private protected ChainNode()
    {
    }

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
            target.NextNode = DeepClone(NextNode);
        }
    }

    protected static ChainNode DeepClone(ChainNode root)
    {
        var rootFrame = new CloneFrame(root);
        var work = new Stack<CloneFrame>();
        work.Push(rootFrame);

        while (work.Count > 0)
        {
            var frame = work.Peek();
            if (!frame.ChildrenQueued)
            {
                frame.QueueChildren(work);
                continue;
            }

            work.Pop();
            frame.Build();
        }

        return rootFrame.Clone!;
    }

    private sealed class CloneFrame
    {
        private readonly ChainNode _source;

        public CloneFrame(ChainNode source)
        {
            _source = source;
        }

        public bool ChildrenQueued { get; private set; }
        public ChainNode? Clone { get; private set; }

        private CloneFrame? _inner;
        private CloneFrame? _separator;
        private CloneFrame? _next;
        private List<CloneFrame>? _alternatives;

        public void QueueChildren(Stack<CloneFrame> work)
        {
            if (_source is OptionalNode optional)
            {
                _inner = new CloneFrame(optional.Inner);
                work.Push(_inner);
            }

            if (_source is RepetitionNode repetition)
            {
                _inner = new CloneFrame(repetition.Inner);
                work.Push(_inner);
                if (repetition.Separator is not null)
                {
                    _separator = new CloneFrame(repetition.Separator);
                    work.Push(_separator);
                }
            }

            if (_source is AlternationNode alternation)
            {
                _alternatives = new List<CloneFrame>(alternation.Alternatives.Count);
                foreach (var alternative in alternation.Alternatives)
                {
                    var altFrame = new CloneFrame(alternative);
                    _alternatives.Add(altFrame);
                    work.Push(altFrame);
                }
            }

            if (_source.NextNode is not null)
            {
                _next = new CloneFrame(_source.NextNode);
                work.Push(_next);
            }

            ChildrenQueued = true;
        }

        public void Build()
        {
            ChainNode clone = _source switch
            {
                OptionalNode => new OptionalNode(_inner!.Clone!),
                RepetitionNode repetition => new RepetitionNode(_inner!.Clone!,
                                                                repetition.Min,
                                                                repetition.Max,
                                                                _separator?.Clone),
                AlternationNode => new AlternationNode(_alternatives!.Select(f => f.Clone!).ToArray()),
                ConjunctiveNode conjunctive => new ConjunctiveNode(conjunctive.Name, conjunctive.ConjunctiveKind),
                WordNode word => new WordNode(word.Name),
                _ => throw new NotSupportedException($"Unknown chain node type {_source.GetType()}."),
            };

            clone.SynonymList.AddRange(_source.SynonymList);
            clone.TargetList.AddRange(_source.TargetList);
            if (_next is not null)
            {
                clone.NextNode = _next.Clone;
            }

            Clone = clone;
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
