using Vice.Nodes;
using Vice.Parser;

namespace Vice.Core;

public record TargetDef(string Name, bool Required = true, bool Variadic = false) : ITargetDescriptor
{

    public static ChainNode operator *(string word, TargetDef target)
    {
        ChainNode node = new WordNode(word);
        node.TargetList.Add(target);
        return node;
    }

    public static ChainNode operator *(ChainNode node, TargetDef target)
    {
        var tail = node;
        while (tail.NextNode is not null)
        {
            tail = tail.NextNode;
        }

        tail.TargetList.Add(target);
        return node;
    }
}
