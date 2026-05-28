using Vice.Parser;

namespace Vice.Nodes;

public sealed class WordNode : ChainNode
{
    private readonly string _name;

    public WordNode(string name)
    {
        _name = name;
    }

    public override string Name => _name;
    public override ChainNodeKind Kind => ChainNodeKind.Word;

    public override ChainNode Clone()
    {
        var clone = new WordNode(_name);
        CopyTo(clone);
        return clone;
    }
}
