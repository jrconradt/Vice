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
            if (current.Kind == ChainNodeKind.Conjunctive && current.ConjunctiveKind == ConjunctiveKind.Piping)
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
            if (current.Kind == ChainNodeKind.Conjunctive && current.ConjunctiveKind == ConjunctiveKind.Piping)
            {
                stages.Add(new PipelineStageInfo(stageNumber, string.Join(" ", parts), targets, operatorWord));
                stageNumber++;
                operatorWord = current.Name;
                parts = new List<string>();
                targets = new List<TargetEntry>();
                continue;
            }

            parts.Add(RenderNode(current));

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
        for (var current = root; current is not null; current = current.Next)
        {
            CollectTargets(current, targets, forceOptional: false);
        }
        return targets;
    }

    public static string Render(this IChainDescriptor root)
    {
        var parts = new List<string>();
        for (var current = root; current is not null; current = current.Next)
        {
            parts.Add(RenderNode(current));
        }
        return string.Join(" ", parts);
    }

    private static string RenderNode(IChainDescriptor node) => node.Kind switch
    {
        ChainNodeKind.Conjunctive => RenderConjunctive(node),
        ChainNodeKind.Optional => "[" + (node.OptionalInner is not null ? node.OptionalInner.Render() : string.Empty) + "]",
        ChainNodeKind.Alternation => "(" + string.Join("|", node.Alternatives.Select(a => a.Render())) + ")",
        ChainNodeKind.Repetition => RenderRepetition(node),
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

    private static string RenderRepetition(IChainDescriptor node)
    {
        var inner = node.RepetitionInner is not null ? node.RepetitionInner.Render() : string.Empty;
        var min = node.RepetitionBounds.Min;

        if (node.RepetitionSeparator is not null)
        {
            var sep = node.RepetitionSeparator.Render();
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

    private static void CollectTargets(IChainDescriptor node, List<TargetEntry> targets, bool forceOptional)
    {
        foreach (var target in node.Targets)
        {
            targets.Add(BuildTargetEntry(target, forceOptional, asPlaceholder: false));
        }

        switch (node.Kind)
        {
            case ChainNodeKind.Optional:
                if (node.OptionalInner is not null)
                {
                    CollectChainTargets(node.OptionalInner, targets, forceOptional: true);
                }
                break;

            case ChainNodeKind.Alternation:
                foreach (var alt in node.Alternatives)
                {
                    CollectChainTargets(alt, targets, forceOptional);
                }
                break;

            case ChainNodeKind.Repetition:
                if (node.RepetitionInner is not null)
                {
                    CollectChainTargets(node.RepetitionInner, targets, forceOptional);
                }
                break;
        }
    }

    private static void CollectChainTargets(IChainDescriptor root, List<TargetEntry> targets, bool forceOptional)
    {
        for (var current = root; current is not null; current = current.Next)
        {
            CollectTargets(current, targets, forceOptional);
        }
    }
}
