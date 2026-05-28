using Vice.Commands;
using Vice.Parser;

namespace Vice.Execution;

internal static class PipelineSplitter
{
    public static List<PipelineStage> Split(CommandChain chain, CommandRegistration registration)
    {
        var stages = new List<PipelineStage>();
        var currentTargets = new Dictionary<string, string>();
        var currentNodes = new List<ResolvedCommand>();
        ConjunctiveKind? currentOperator = null;
        string? currentOperatorWord = null;
        int stageIndex = 0;

        foreach (var node in chain.Nodes)
        {
            if (node.Descriptor.Kind == ChainNodeKind.Conjunctive &&
                node.Descriptor.ConjunctiveKind == ConjunctiveKind.Piping)
            {
                var handler = GetHandler(registration, stageIndex);
                stages.Add(CreateStage(currentOperator, currentOperatorWord, currentTargets, currentNodes, handler, registration, stageIndex));
                stageIndex++;

                currentTargets = new Dictionary<string, string>();
                currentNodes = new List<ResolvedCommand>();
                currentOperator = ConjunctiveKind.Piping;
                currentOperatorWord = node.Descriptor.Name;
            }
            else
            {
                foreach (var kv in node.TargetValues)
                {
                    currentTargets[kv.Key] = kv.Value;
                }

                currentNodes.Add(node);
            }
        }

        var finalHandler = GetHandler(registration, stageIndex);
        stages.Add(CreateStage(currentOperator, currentOperatorWord, currentTargets, currentNodes, finalHandler, registration, stageIndex));

        return stages;
    }

    public static bool HasPipeline(CommandChain chain)
    {
        foreach (var node in chain.Nodes)
        {
            if (node.Descriptor.Kind == ChainNodeKind.Conjunctive &&
                node.Descriptor.ConjunctiveKind == ConjunctiveKind.Piping)
            {
                return true;
            }
        }
        return false;
    }

    private static PipelineStage CreateStage(
        ConjunctiveKind? op, string? operatorWord,
        Dictionary<string, string> targets,
        List<ResolvedCommand> nodes,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        CommandRegistration registration,
        int stageIndex)
    {
        if (registration.Mode == StageMode.Classic)
        {
            return new PipelineStage(op, operatorWord, targets, handler, nodes);
        }

        var mode = (stageIndex > 0 && registration.Launcher is { HasConsumer: true })
            ? StageMode.StreamConsumer
            : registration.Mode;

        return new PipelineStage(op, operatorWord, targets, handler,
            mode, registration.StreamOptions, registration.Launcher, nodes);
    }

    internal static List<PipelineStage> BuildFromSegments(
        IReadOnlyList<PipelineSegment> segments,
        IReadOnlyList<CommandRegistration> registrations)
    {
        var stages = new List<PipelineStage>(segments.Count);
        foreach (var s in segments)
        {
            var reg = registrations[s.MatchedRegistrationIndex];
            var targets = s.Chain.AllTargetValues();
            var nodes = s.Chain.Nodes;

            if (reg.Mode != StageMode.Classic)
            {
                stages.Add(new PipelineStage(null, s.OperatorWord, targets,
                    reg.Handler, reg.Mode, reg.StreamOptions, reg.Launcher, nodes));
            }
            else
            {
                stages.Add(new PipelineStage(null, s.OperatorWord, targets, reg.Handler, nodes));
            }
        }
        return stages;
    }

    private static Func<CommandContext, CancellationToken, Task<int>> GetHandler(
        CommandRegistration registration, int stageIndex)
    {
        if (registration.StageHandlers is not null &&
            registration.StageHandlers.TryGetValue(stageIndex, out var stageHandler))
        {
            return stageHandler;
        }

        return registration.Handler;
    }
}
