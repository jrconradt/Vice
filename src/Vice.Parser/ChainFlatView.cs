namespace Vice.Parser;

public readonly record struct TargetEntry(string Name, string Info, bool IsOptional);

public sealed record PipelineStageInfo(
    int StageNumber,
    string FormattedChain,
    IReadOnlyList<TargetEntry> Targets,
    string? OperatorWord);

public static class ChainFlatView
{
    public static bool HasPipelineStages(this IChainDescriptor root)
    {
        for (var current = root; current is not null; current = current.Next)
        {
            if (current.Kind == ChainNodeKind.Conjunctive && current.ConjunctiveKind == ConjunctiveKind.StageSeparator)
            {
                return true;
            }
        }
        return false;
    }

    public static IReadOnlyList<PipelineStageInfo> GetPipelineStages(this IChainDescriptor root)
    {
        if (!root.HasPipelineStages())
        {
            return Array.Empty<PipelineStageInfo>();
        }

        var stages = new List<PipelineStageInfo>();
        var stageNumber = 1;
        string? operatorWord = null;
        var parts = new List<string>();
        var targets = new List<TargetEntry>();

        for (var current = root; current is not null; current = current.Next)
        {
            if (current.Kind == ChainNodeKind.Conjunctive && current.ConjunctiveKind == ConjunctiveKind.StageSeparator)
            {
                stages.Add(new PipelineStageInfo(stageNumber, string.Join(" ", parts), targets, operatorWord));
                stageNumber++;
                operatorWord = current.Name;
                parts = new List<string>();
                targets = new List<TargetEntry>();
                continue;
            }

            parts.Add(RenderSingleNode(current));

            foreach (var target in current.Targets)
            {
                targets.Add(BuildTargetEntry(target, forceOptional: false, asPlaceholder: true));
            }
        }

        stages.Add(new PipelineStageInfo(stageNumber, string.Join(" ", parts), targets, operatorWord));
        return stages;
    }

    public static IReadOnlyList<TargetEntry> GetTargets(this IChainDescriptor root)
    {
        var targets = new List<TargetEntry>();
        var pending = new List<(IChainDescriptor Node, bool ForceOptional)>();
        for (var current = root; current is not null; current = current.Next)
        {
            pending.Add((current, false));
        }

        var work = new Stack<(IChainDescriptor Node, bool ForceOptional)>();
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            work.Push(pending[i]);
        }

        while (work.Count > 0)
        {
            var (node, forceOptional) = work.Pop();

            foreach (var target in node.Targets)
            {
                targets.Add(BuildTargetEntry(target, forceOptional, asPlaceholder: false));
            }

            var children = CollectChildFrames(node, forceOptional);
            for (var i = children.Count - 1; i >= 0; i--)
            {
                work.Push(children[i]);
            }
        }

        return targets;
    }

    private static List<(IChainDescriptor Node, bool ForceOptional)> CollectChildFrames(IChainDescriptor node, bool forceOptional)
    {
        var children = new List<(IChainDescriptor Node, bool ForceOptional)>();
        switch (node.Kind)
        {
            case ChainNodeKind.Optional:
                for (var current = node.OptionalInner; current is not null; current = current.Next)
                {
                    children.Add((current, true));
                }
                break;

            case ChainNodeKind.Alternation:
                foreach (var alt in node.Alternatives)
                {
                    for (var current = alt; current is not null; current = current.Next)
                    {
                        children.Add((current, forceOptional));
                    }
                }
                break;

            case ChainNodeKind.Repetition:
                for (var current = node.RepetitionInner; current is not null; current = current.Next)
                {
                    children.Add((current, forceOptional));
                }
                break;
        }
        return children;
    }

    public static string Render(this IChainDescriptor root)
    {
        var rendered = new Dictionary<IChainDescriptor, string>(ReferenceEqualityComparer.Instance);
        PopulateRenderedChain(root, rendered);
        return rendered[root];
    }

    private static string RenderSingleNode(IChainDescriptor node)
    {
        var rendered = new Dictionary<IChainDescriptor, string>(ReferenceEqualityComparer.Instance);
        var work = new Stack<(IChainDescriptor Root, bool Expanded)>();
        PushChildChains(node, rendered, work);
        DrainRenderWork(rendered, work);
        return RenderNode(node, rendered);
    }

    private static void PopulateRenderedChain(IChainDescriptor root, Dictionary<IChainDescriptor, string> rendered)
    {
        var work = new Stack<(IChainDescriptor Root, bool Expanded)>();
        work.Push((root, false));
        DrainRenderWork(rendered, work);
    }

    private static void DrainRenderWork(
        Dictionary<IChainDescriptor, string> rendered,
        Stack<(IChainDescriptor Root, bool Expanded)> work)
    {
        while (work.Count > 0)
        {
            var (chainRoot, expanded) = work.Pop();
            if (rendered.ContainsKey(chainRoot))
            {
                continue;
            }

            if (!expanded)
            {
                work.Push((chainRoot, true));
                for (var current = chainRoot; current is not null; current = current.Next)
                {
                    PushChildChains(current, rendered, work);
                }
                continue;
            }

            var parts = new List<string>();
            for (var current = chainRoot; current is not null; current = current.Next)
            {
                parts.Add(RenderNode(current, rendered));
            }
            rendered[chainRoot] = string.Join(" ", parts);
        }
    }

    private static void PushChildChains(
        IChainDescriptor node,
        Dictionary<IChainDescriptor, string> rendered,
        Stack<(IChainDescriptor Root, bool Expanded)> work)
    {
        switch (node.Kind)
        {
            case ChainNodeKind.Optional:
                if (node.OptionalInner is not null && !rendered.ContainsKey(node.OptionalInner))
                {
                    work.Push((node.OptionalInner, false));
                }
                break;

            case ChainNodeKind.Alternation:
                foreach (var alt in node.Alternatives)
                {
                    if (!rendered.ContainsKey(alt))
                    {
                        work.Push((alt, false));
                    }
                }
                break;

            case ChainNodeKind.Repetition:
                if (node.RepetitionInner is not null && !rendered.ContainsKey(node.RepetitionInner))
                {
                    work.Push((node.RepetitionInner, false));
                }
                if (node.RepetitionSeparator is not null && !rendered.ContainsKey(node.RepetitionSeparator))
                {
                    work.Push((node.RepetitionSeparator, false));
                }
                break;
        }
    }

    private static string RenderNode(IChainDescriptor node, Dictionary<IChainDescriptor, string> rendered) => node.Kind switch
    {
        ChainNodeKind.Conjunctive => RenderConjunctive(node),
        ChainNodeKind.Optional => "[" + (node.OptionalInner is not null ? rendered[node.OptionalInner] : string.Empty) + "]",
        ChainNodeKind.Alternation => "(" + string.Join("|", node.Alternatives.Select(a => rendered[a])) + ")",
        ChainNodeKind.Repetition => RenderRepetition(node, rendered),
        _ => RenderWord(node),
    };

    private static string RenderConjunctive(IChainDescriptor node)
    {
        var pieces = new List<string> { node.Name };
        foreach (var target in node.Targets)
        {
            pieces.Add(target.Required ? $"{{{target.Name}}}" : $"[{{{target.Name}}}]");
        }
        return string.Join(" ", pieces);
    }

    private static string RenderRepetition(IChainDescriptor node, Dictionary<IChainDescriptor, string> rendered)
    {
        var inner = node.RepetitionInner is not null ? rendered[node.RepetitionInner] : string.Empty;
        var min = node.RepetitionBounds.Min;

        if (node.RepetitionSeparator is not null)
        {
            var sep = rendered[node.RepetitionSeparator];
            var tail = "[" + sep + " " + inner + "...]";
            return min >= 1
                ? inner + " " + tail
                : "[" + inner + " " + tail + "]";
        }

        return min >= 1 ? inner + "..." : "[" + inner + "...]";
    }

    private static string RenderWord(IChainDescriptor node)
    {
        var names = new List<string> { node.Name };
        names.AddRange(node.Synonyms);
        var nameStr = names.Count > 1 ? string.Join("|", names) : names[0];

        var pieces = new List<string> { nameStr };
        foreach (var target in node.Targets)
        {
            pieces.Add(target.Required ? $"{{{target.Name}}}" : $"[{{{target.Name}}}]");
        }
        return string.Join(" ", pieces);
    }

    private static TargetEntry BuildTargetEntry(ITargetDescriptor target, bool forceOptional, bool asPlaceholder)
    {
        var name = asPlaceholder
            ? (target.Required && !forceOptional ? $"{{{target.Name}}}" : $"[{{{target.Name}}}]")
            : $"{{{target.Name}}}";

        var required = target.Required && !forceOptional;
        var info = required ? "required" : "optional";
        if (target.Variadic)
        {
            info += ", variadic";
        }

        return new TargetEntry(name, info, !required);
    }
}
