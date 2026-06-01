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

    private sealed class ExpandFrame
    {
        public required ChainNode? Chain { get; init; }
        public bool Expanded { get; set; }
        public List<int> ChildSlots { get; } = new();
        public ChainNode? PrependHead { get; set; }
    }

    private static IEnumerable<ChainNode?> ExpandToLinear(ChainNode? chain, int repetitionUnrollDepth = 3)
    {
        var results = new Dictionary<int, List<ChainNode?>>();
        var rootFrame = new ExpandFrame { Chain = chain };
        var frames = new Dictionary<int, ExpandFrame>();
        var nextSlot = 0;
        var rootSlot = nextSlot++;
        frames[rootSlot] = rootFrame;

        var stack = new Stack<int>();
        stack.Push(rootSlot);

        while (stack.Count > 0)
        {
            var slot = stack.Peek();
            var frame = frames[slot];

            if (!frame.Expanded)
            {
                frame.Expanded = true;
                var current = frame.Chain;

                if (current is null)
                {
                    results[slot] = new List<ChainNode?> { null };
                    stack.Pop();
                    continue;
                }

                switch (current)
                {
                    case WordNode or ConjunctiveNode:
                        {
                            frame.PrependHead = current;
                            var childSlot = nextSlot++;
                            frames[childSlot] = new ExpandFrame { Chain = current.NextNode };
                            frame.ChildSlots.Add(childSlot);
                            stack.Push(childSlot);
                            continue;
                        }
                    case OptionalNode opt:
                        {
                            var firstSlot = nextSlot++;
                            frames[firstSlot] = new ExpandFrame { Chain = SpliceBefore(opt.Inner, opt.NextNode) };
                            frame.ChildSlots.Add(firstSlot);

                            var secondSlot = nextSlot++;
                            frames[secondSlot] = new ExpandFrame { Chain = opt.NextNode };
                            frame.ChildSlots.Add(secondSlot);

                            stack.Push(secondSlot);
                            stack.Push(firstSlot);
                            continue;
                        }
                    case AlternationNode alt:
                        {
                            for (var i = alt.Alternatives.Count - 1; i >= 0; i--)
                            {
                                var altSlot = nextSlot++;
                                frames[altSlot] = new ExpandFrame { Chain = SpliceBefore(alt.Alternatives[i], alt.NextNode) };
                                frame.ChildSlots.Insert(0, altSlot);
                                stack.Push(altSlot);
                            }
                            continue;
                        }
                    case RepetitionNode rep:
                        {
                            var upper = rep.Max < repetitionUnrollDepth ? rep.Max : repetitionUnrollDepth;
                            for (var count = upper; count >= rep.Min; count--)
                            {
                                var unrolled = BuildRepetition(rep.Inner, rep.Separator, count);
                                var spliced = unrolled is null ? rep.NextNode?.Clone() : SpliceBefore(unrolled, rep.NextNode);
                                var repSlot = nextSlot++;
                                frames[repSlot] = new ExpandFrame { Chain = spliced };
                                frame.ChildSlots.Insert(0, repSlot);
                                stack.Push(repSlot);
                            }
                            continue;
                        }
                    default:
                        {
                            results[slot] = new List<ChainNode?>();
                            stack.Pop();
                            continue;
                        }
                }
            }

            var combined = new List<ChainNode?>();
            if (frame.PrependHead is not null)
            {
                foreach (var childSlot in frame.ChildSlots)
                {
                    foreach (var tail in results[childSlot])
                    {
                        var head = CloneHeadOnly(frame.PrependHead);
                        head.NextNode = tail;
                        combined.Add(head);
                    }
                }
            }
            else
            {
                foreach (var childSlot in frame.ChildSlots)
                {
                    combined.AddRange(results[childSlot]);
                }
            }

            results[slot] = combined;
            stack.Pop();
        }

        return results[rootSlot];
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
        var tail = result;
        while (tail.NextNode is not null)
        {
            tail = tail.NextNode;
        }

        for (var i = 1; i < count; i++)
        {
            var next = separator is null ? inner.Clone() : SpliceBefore(separator, inner)!;
            tail.NextNode = next;
            while (tail.NextNode is not null)
            {
                tail = tail.NextNode;
            }
        }
        return result;
    }

    private static void Insert(CompletionNode parent, ChainNode? chain, string description)
    {
        var current = parent;
        var link = chain;
        while (link is not null)
        {
            var token = link.Name;
            if (!current.Children.TryGetValue(token, out var child))
            {
                child = current.Children[token] = new CompletionNode { Token = token };
                child.Synonyms.AddRange(link.SynonymList);
                child.TargetCount = link.TargetList.Count;
            }
            else
            {
                foreach (var syn in link.SynonymList)
                {
                    if (!child.Synonyms.Contains(syn))
                    {
                        child.Synonyms.Add(syn);
                    }
                }

                if (link.TargetList.Count > child.TargetCount)
                {
                    child.TargetCount = link.TargetList.Count;
                }
            }

            if (link.NextNode is null)
            {
                child.IsTerminal = true;
                child.Description ??= description;
            }

            current = child;
            link = link.NextNode;
        }
    }

    private static void AssignStateIds(CompletionNode node, string id)
    {
        var stack = new Stack<(CompletionNode Node, string Id)>();
        stack.Push((node, id));
        while (stack.Count > 0)
        {
            var (current, currentId) = stack.Pop();
            current.StateId = currentId;
            foreach (var (token, child) in current.Children)
            {
                stack.Push((child, $"{currentId}__{SanitizeStateSegment(token)}"));
            }
        }
    }

    private static string SanitizeStateSegment(string s)
        => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
}
