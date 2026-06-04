namespace Vice.Parser;

internal static class PipelineSplitter
{
    public static bool ContainsPipingToken(IReadOnlyList<Token> tokens)
    {
        foreach (var t in tokens)
        {
            if (CommandResolver.PipingWords.Contains(t.Value))
            {
                return true;
            }
        }

        return false;
    }

    public static ParseResult? TryStagedMatch(
        IReadOnlyList<Token> remaining,
        IReadOnlyList<IChainDescriptor> registrations,
        IReadOnlyDictionary<string, string?> globals,
        bool partialsEnabled)
    {
        var headIndex = HeadIndex.Build(registrations);
        var splits = SplitAtPipingTokens(remaining);
        var segments = new List<PipelineSegment>(splits.Count);

        foreach (var (segTokens, operatorWord) in splits)
        {
            if (segTokens.Count == 0)
            {
                return null;
            }

            bool found = false;
            for (int i = 0; i < registrations.Count; i++)
            {
                var (success, _) = ChainMatcher.TryMatch(segTokens, registrations[i], globals, i, headIndex, partialsEnabled);
                if (success is not null)
                {
                    segments.Add(new PipelineSegment(success.Chain!, i, operatorWord));
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return null;
            }
        }

        return new ParseResult(segments, globals);
    }

    private static List<(List<Token> Tokens, string? OperatorWord)> SplitAtPipingTokens(
        IReadOnlyList<Token> tokens)
    {
        var result = new List<(List<Token>, string?)>();
        var current = new List<Token>();
        string? pendingOp = null;

        foreach (var token in tokens)
        {
            if (CommandResolver.PipingWords.Contains(token.Value))
            {
                result.Add((current, pendingOp));
                current = new List<Token>();
                pendingOp = token.Value.ToLowerInvariant();
            }
            else
            {
                current.Add(token);
            }
        }

        result.Add((current, pendingOp));
        return result;
    }

    private static IReadOnlyList<Token> Slice(IReadOnlyList<Token> tokens, int start)
    {
        var list = new List<Token>(tokens.Count - start);
        for (int i = start; i < tokens.Count; i++)
        {
            list.Add(tokens[i]);
        }

        return list;
    }

    private static (ParseResult? result, int consumed, int regIndex) FindBestPrefixMatch(
        IReadOnlyList<Token> sliced,
        IReadOnlyList<IChainDescriptor> registrations,
        IReadOnlyDictionary<string, string?> globals,
        HeadIndex heads,
        bool partialsEnabled)
    {
        ParseResult? best = null;
        int bestConsumed = 0;
        int bestIndex = -1;

        for (int i = 0; i < registrations.Count; i++)
        {
            var (success, consumed) = ChainMatcher.TryPrefixMatch(sliced, registrations[i], globals, i, heads, partialsEnabled);
            if (success is null || consumed <= bestConsumed)
            {
                continue;
            }

            best = success;
            bestConsumed = consumed;
            bestIndex = i;
        }

        return (best, bestConsumed, bestIndex);
    }

    private enum PipelineContinuation { Stop, EndOfInput, Explicit, Implicit }

    private static (PipelineContinuation kind, string? op, int newPos) ClassifyPipelineBoundary(
        IReadOnlyList<Token> remaining, int pos, HeadIndex verbHeads)
    {
        if (pos >= remaining.Count)
        {
            return (PipelineContinuation.EndOfInput, null, pos);
        }

        var next = remaining[pos].Value;
        if (CommandResolver.PipingWords.Contains(next))
        {
            var afterOp = pos + 1;
            if (afterOp >= remaining.Count)
            {
                return (PipelineContinuation.Stop, null, pos);
            }
            return (PipelineContinuation.Explicit, next.ToLowerInvariant(), afterOp);
        }

        if (verbHeads.ContainsExact(next))
        {
            return (PipelineContinuation.Implicit, "then", pos);
        }

        return (PipelineContinuation.Stop, null, pos);
    }

    public static ParseResult? TryUnifiedPipelineMatch(
        IReadOnlyList<Token> remaining,
        IReadOnlyList<IChainDescriptor> registrations,
        IReadOnlyDictionary<string, string?> globals,
        bool partialsEnabled)
    {
        var verbHeads = HeadIndex.Build(registrations);
        var segments = new List<PipelineSegment>();
        int pos = 0;
        string? pendingOp = null;

        while (pos < remaining.Count)
        {
            var (result, consumed, regIndex) = FindBestPrefixMatch(Slice(remaining, pos), registrations, globals, verbHeads, partialsEnabled);
            if (result is null || consumed == 0)
            {
                return null;
            }

            segments.Add(new PipelineSegment(result.Chain!, regIndex, pendingOp));
            pos += consumed;

            var (kind, nextOp, newPos) = ClassifyPipelineBoundary(remaining, pos, verbHeads);
            switch (kind)
            {
                case PipelineContinuation.EndOfInput:
                    break;
                case PipelineContinuation.Explicit:
                    pendingOp = nextOp;
                    pos = newPos;
                    continue;
                case PipelineContinuation.Implicit:
                    pendingOp = nextOp;
                    continue;
                case PipelineContinuation.Stop:
                default:
                    return null;
            }

            break;
        }

        if (segments.Count == 0)
        {
            return null;
        }

        if (segments.Count == 1)
        {
            return new ParseResult(segments[0].Chain, globals, Array.Empty<string>(), segments[0].MatchedRegistrationIndex);
        }

        return new ParseResult(segments, globals);
    }
}
