using Vice.Parser;

namespace Vice.Nodes;

public sealed class ConjunctiveNode : ChainNode
{
    private readonly string _word;
    private readonly ConjunctiveKind _conjunctiveKind;

    public ConjunctiveNode(string word, ConjunctiveKind conjunctiveKind = ConjunctiveKind.Preposition)
    {
        _word = word;
        _conjunctiveKind = conjunctiveKind;
    }

    public override string Name => _word;
    public override ChainNodeKind Kind => ChainNodeKind.Conjunctive;
    public ConjunctiveKind ConjunctiveKind => _conjunctiveKind;

    public override ChainNode Clone()
    {
        var clone = new ConjunctiveNode(_word, _conjunctiveKind);
        CopyTo(clone);
        return clone;
    }
}
