using Vice.Commands;
using Vice.Contracts;
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
        public List<ExpandFrame> Children { get; } = new();
        public ChainNode? PrependHead { get; set; }
        public List<ChainNode?>? Result { get; set; }
    }

    private static IEnumerable<ChainNode?> ExpandToLinear(ChainNode? chain, int repetitionUnrollDepth = 3)
    {
        var rootFrame = new ExpandFrame { Chain = chain };
        var stack = new Stack<ExpandFrame>();
        stack.Push(rootFrame);

        while (stack.Count > 0)
        {
            var frame = stack.Peek();

            if (!frame.Expanded)
            {
                frame.Expanded = true;
                var current = frame.Chain;

                if (current is null)
                {
                    frame.Result = new List<ChainNode?> { null };
                    stack.Pop();
                    continue;
                }

                switch (current)
                {
                    case WordNode or ConjunctiveNode:
                        {
                            frame.PrependHead = current;
                            PushChildren(stack, frame, ExpandConjunctive(current));
                            continue;
                        }
                    case OptionalNode opt:
                        {
                            PushChildren(stack, frame, ExpandOptional(opt));
                            continue;
                        }
                    case AlternationNode alt:
                        {
                            PushChildren(stack, frame, ExpandAlternation(alt));
                            continue;
                        }
                    case RepetitionNode rep:
                        {
                            PushChildren(stack, frame, ExpandRepetition(rep, repetitionUnrollDepth));
                            continue;
                        }
                    default:
                        {
                            frame.Result = new List<ChainNode?>();
                            stack.Pop();
                            continue;
                        }
                }
            }

            var combined = new List<ChainNode?>();
            if (frame.PrependHead is not null)
            {
                foreach (var child in frame.Children)
                {
                    foreach (var tail in child.Result!)
                    {
                        var head = CloneHeadOnly(frame.PrependHead);
                        head.NextNode = tail;
                        combined.Add(head);
                    }
                }
            }
            else
            {
                foreach (var child in frame.Children)
                {
                    combined.AddRange(child.Result!);
                }
            }

            frame.Result = combined;
            stack.Pop();
        }

        return rootFrame.Result!;
    }

    private static void PushChildren(
        Stack<ExpandFrame> stack,
        ExpandFrame frame,
        List<ExpandFrame> children)
    {
        foreach (var child in children)
        {
            frame.Children.Add(child);
        }

        for (var i = children.Count - 1; i >= 0; i--)
        {
            stack.Push(children[i]);
        }
    }

    private static List<ExpandFrame> ExpandConjunctive(ChainNode node)
        => new() { new ExpandFrame { Chain = node.NextNode } };

    private static List<ExpandFrame> ExpandOptional(OptionalNode opt)
        => new()
        {
            new ExpandFrame { Chain = SpliceBefore(opt.Inner, opt.NextNode) },
            new ExpandFrame { Chain = opt.NextNode },
        };

    private static List<ExpandFrame> ExpandAlternation(AlternationNode alt)
    {
        var children = new List<ExpandFrame>();
        foreach (var alternative in alt.Alternatives)
        {
            children.Add(new ExpandFrame { Chain = SpliceBefore(alternative, alt.NextNode) });
        }

        return children;
    }

    private static List<ExpandFrame> ExpandRepetition(RepetitionNode rep, int repetitionUnrollDepth)
    {
        var upper = rep.Max < repetitionUnrollDepth ? rep.Max : repetitionUnrollDepth;
        var children = new List<ExpandFrame>();
        for (var count = rep.Min; count <= upper; count++)
        {
            var unrolled = BuildRepetition(rep.Inner, rep.Separator, count);
            var spliced = unrolled is null ? rep.NextNode?.Clone() : SpliceBefore(unrolled, rep.NextNode);
            children.Add(new ExpandFrame { Chain = spliced });
        }

        return children;
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
