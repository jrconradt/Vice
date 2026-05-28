using Vice.Commands;
using Vice.Nodes;
using Vice.Options;

namespace Vice.Completions;

internal static class CompletionModelBuilder
{
    public static CompletionTrie Build(
        string appName,
        IReadOnlyList<CommandRegistration> registrations,
        IReadOnlyCollection<GlobalOption> globalOptions)
    {
        var root = new CompletionNode();

        var depth1Descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reg in registrations)
        {
            var headToken = ExtractHeadToken(reg.Chain);
            if (headToken is not null && !depth1Descriptions.ContainsKey(headToken))
            {
                depth1Descriptions[headToken] = reg.Description;
            }

            foreach (var linear in ExpandToLinear(reg.Chain))
            {
                Insert(root, linear, reg.Description);
            }
        }

        foreach (var child in root.Children.Values)
        {
            if (child.Description is null && depth1Descriptions.TryGetValue(child.Token, out var d))
            {
                child.Description = d;
            }
        }

        AssignStateIds(root, "root");

        var globals = globalOptions
            .OrderBy(o => o.Name, StringComparer.Ordinal)
            .Select(o => new GlobalOptionEntry(
                o.Name,
                o.Description,
                o.TakesValue,
                o.Aliases))
            .ToList();

        return new CompletionTrie(appName, root, globals);
    }

    private static string? ExtractHeadToken(ChainNode? chain)
    {
        var node = chain;
        while (true)
        {
            switch (node)
            {
                case ConjunctiveNode:
                    node = node.NextNode;
                    continue;
                case OptionalNode opt:
                    node = opt.Inner;
                    continue;
                case AlternationNode alt:
                    node = alt.Alternatives.Count > 0 ? alt.Alternatives[0] : null;
                    continue;
                case RepetitionNode rep:
                    node = rep.Inner;
                    continue;
                default:
                    return node is WordNode w ? w.Name : null;
            }
        }
    }

    private static IEnumerable<ChainNode?> ExpandToLinear(ChainNode? chain, int repetitionUnrollDepth = 3)
    {
        if (chain is null)
        {
            yield return null;
            yield break;
        }

        switch (chain)
        {
            case WordNode or ConjunctiveNode:
                {
                    foreach (var tail in ExpandToLinear(chain.NextNode, repetitionUnrollDepth))
                    {
                        var head = CloneHeadOnly(chain);
                        head.NextNode = tail;
                        yield return head;
                    }
                    yield break;
                }
            case OptionalNode opt:
                {
                    foreach (var expansion in ExpandToLinear(SpliceBefore(opt.Inner, opt.NextNode), repetitionUnrollDepth))
                    {
                        yield return expansion;
                    }

                    foreach (var expansion in ExpandToLinear(opt.NextNode, repetitionUnrollDepth))
                    {
                        yield return expansion;
                    }

                    yield break;
                }
            case AlternationNode alt:
                {
                    foreach (var alternative in alt.Alternatives)
                    {
                        foreach (var expansion in ExpandToLinear(SpliceBefore(alternative, alt.NextNode), repetitionUnrollDepth))
                        {
                            yield return expansion;
                        }
                    }

                    yield break;
                }
            case RepetitionNode rep:
                {
                    var upper = rep.Max < repetitionUnrollDepth ? rep.Max : repetitionUnrollDepth;
                    for (var count = rep.Min; count <= upper; count++)
                    {
                        var unrolled = BuildRepetition(rep.Inner, rep.Separator, count);
                        var spliced = unrolled is null ? rep.NextNode?.Clone() : SpliceBefore(unrolled, rep.NextNode);
                        foreach (var expansion in ExpandToLinear(spliced, repetitionUnrollDepth))
                        {
                            yield return expansion;
                        }
                    }
                    yield break;
                }
        }
    }

    private static ChainNode CloneHeadOnly(ChainNode node)
    {
        var saved = node.NextNode;
        node.NextNode = null;
        var clone = node.Clone();
        node.NextNode = saved;
        return clone;
    }

    private static ChainNode? SpliceBefore(ChainNode head, ChainNode? after)
    {
        var headClone = head.Clone();
        if (after is null)
        {
            return headClone;
        }

        var tail = headClone;
        while (tail.NextNode is not null)
        {
            tail = tail.NextNode;
        }

        tail.NextNode = after.Clone();
        return headClone;
    }

    private static ChainNode? BuildRepetition(ChainNode inner, ChainNode? separator, int count)
    {
        if (count <= 0)
        {
            return null;
        }

        var result = inner.Clone();
        for (var i = 1; i < count; i++)
        {
            var next = separator is null ? inner.Clone() : SpliceBefore(separator, inner)!;
            var tail = result;
            while (tail.NextNode is not null)
            {
                tail = tail.NextNode;
            }

            tail.NextNode = next;
        }
        return result;
    }

    private static void Insert(CompletionNode parent, ChainNode? chain, string description)
    {
        if (chain is null)
        {
            return;
        }

        var token = chain.Name;
        if (!parent.Children.TryGetValue(token, out var child))
        {
            child = parent.Children[token] = new CompletionNode { Token = token };
            child.Synonyms.AddRange(chain.SynonymList);
            child.TargetCount = chain.TargetList.Count;
        }
        else
        {
            foreach (var syn in chain.SynonymList)
            {
                if (!child.Synonyms.Contains(syn))
                {
                    child.Synonyms.Add(syn);
                }
            }

            if (chain.TargetList.Count > child.TargetCount)
            {
                child.TargetCount = chain.TargetList.Count;
            }
        }

        if (chain.NextNode is null)
        {
            child.IsTerminal = true;
            child.Description ??= description;
        }
        else
        {
            Insert(child, chain.NextNode, description);
        }
    }

    private static void AssignStateIds(CompletionNode node, string id)
    {
        node.StateId = id;
        foreach (var (token, child) in node.Children)
        {
            AssignStateIds(child, $"{id}__{SanitizeStateSegment(token)}");
        }
    }

    private static string SanitizeStateSegment(string s)
        => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
}
