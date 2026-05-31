namespace Vice.Parser;

public static class CommandResolver
{
    public static ParseResult Resolve(string[] args, IReadOnlyList<IChainDescriptor> registrations,
        IReadOnlySet<string>? valueBearingOptions = null,
        IReadOnlySet<string>? knownFlags = null,
        bool PartialsEnabled = true,
        bool ImplicitPipelinesEnabled = true)
    {
        var tokens = Lexer.Tokenize(args);
        return ResolveTokens(tokens, registrations, valueBearingOptions, knownFlags, PartialsEnabled, ImplicitPipelinesEnabled);
    }

    public static ParseResult Resolve(string input, IReadOnlyList<IChainDescriptor> registrations,
        IReadOnlySet<string>? valueBearingOptions = null,
        IReadOnlySet<string>? knownFlags = null,
        bool PartialsEnabled = true,
        bool ImplicitPipelinesEnabled = true)
    {
        var tokens = Lexer.Tokenize(input);
        return ResolveTokens(tokens, registrations, valueBearingOptions, knownFlags, PartialsEnabled, ImplicitPipelinesEnabled);
    }

    public static ParseResult Resolve(string[] args, IReadOnlyList<IChainDescriptor> registrations,
        OptionRegistry options,
        bool partialsEnabled = true,
        bool implicitPipelinesEnabled = true)
    {
        var tokens = Lexer.Tokenize(args);
        return ResolveTokensWithRegistry(tokens, registrations, options, partialsEnabled, implicitPipelinesEnabled);
    }

    public static ParseResult Resolve(string input, IReadOnlyList<IChainDescriptor> registrations,
        OptionRegistry options,
        bool partialsEnabled = true,
        bool implicitPipelinesEnabled = true)
    {
        var tokens = Lexer.Tokenize(input);
        return ResolveTokensWithRegistry(tokens, registrations, options, partialsEnabled, implicitPipelinesEnabled);
    }

    private static ParseResult ResolveTokensWithRegistry(
        IReadOnlyList<Token> tokens,
        IReadOnlyList<IChainDescriptor> registrations,
        OptionRegistry options,
        bool partialsEnabled,
        bool implicitPipelinesEnabled)
    {
        var (remaining, globals, errors) = GlobalOptionExtractor.Extract(tokens, options);
        if (errors.Count > 0)
        {
            return ParseResult.Error(globals, errors.ToArray());
        }

        return ResolveRemainingTokens(remaining, globals, registrations, partialsEnabled, implicitPipelinesEnabled);
    }

    private static ParseResult ResolveTokens(IReadOnlyList<Token> tokens, IReadOnlyList<IChainDescriptor> registrations,
        IReadOnlySet<string>? valueBearingOptions,
        IReadOnlySet<string>? knownFlags,
        bool partialsEnabled = true,
        bool implicitPipelinesEnabled = true)
    {
        var (remaining, globals, extractorErrors) = GlobalOptionExtractor.Extract(tokens, valueBearingOptions, knownFlags);

        if (extractorErrors.Count > 0)
        {
            return ParseResult.Error(globals, extractorErrors.ToArray());
        }

        return ResolveRemainingTokens(remaining, globals, registrations, partialsEnabled, implicitPipelinesEnabled);
    }

    private static ParseResult ResolveRemainingTokens(
        IReadOnlyList<Token> remaining,
        IReadOnlyDictionary<string, string?> globals,
        IReadOnlyList<IChainDescriptor> registrations,
        bool partialsEnabled,
        bool implicitPipelinesEnabled)
    {
        if (remaining.Count == 0)
        {
            return ParseResult.Error(globals, "No command provided.");
        }

        MatchDiagnostic? ambiguousDiagnostic = null;
        if (partialsEnabled)
        {
            var (_, _, prefixCandidates) = TryPrefixHead(remaining[0].Value, registrations);
            if (prefixCandidates is { Count: > 0 } &&
                !HasExactHeadMatch(remaining[0].Value, registrations))
            {
                ambiguousDiagnostic = new MatchDiagnostic(remaining[0].Value, 0, AmbiguousCandidates: prefixCandidates);
            }
        }

        MatchDiagnostic? bestDiagnostic = null;

        for (int i = 0; i < registrations.Count; i++)
        {
            var (success, diagnostic) = TryMatch(remaining, registrations[i], globals, i, registrations, partialsEnabled);
            if (success is not null)
            {
                return success;
            }

            if (diagnostic is not null &&
                (bestDiagnostic is null
                 || diagnostic.AmbiguousCandidates is { Count: > 0 }
                 || diagnostic.MatchedDepth > bestDiagnostic.MatchedDepth))
            {
                bestDiagnostic = diagnostic;
            }
        }

        if (implicitPipelinesEnabled)
        {
            var unified = TryUnifiedPipelineMatch(remaining, registrations, globals, partialsEnabled);
            if (unified is not null)
            {
                return unified;
            }
        }

        if (ContainsPipingToken(remaining))
        {
            var staged = TryStagedMatch(remaining, registrations, globals, partialsEnabled);
            if (staged is not null)
            {
                return staged;
            }
        }

        if (ambiguousDiagnostic is not null &&
            (bestDiagnostic is null || bestDiagnostic.AmbiguousCandidates is null or { Count: 0 }))
        {
            return ParseResult.Error(globals, ambiguousDiagnostic, ambiguousDiagnostic.FormatError());
        }

        if (bestDiagnostic is not null && bestDiagnostic.AmbiguousCandidates is { Count: > 0 })
        {
            return ParseResult.Error(globals, bestDiagnostic, bestDiagnostic.FormatError());
        }

        if (bestDiagnostic is not null && bestDiagnostic.MatchedDepth > 0)
        {
            return ParseResult.Error(globals, bestDiagnostic, bestDiagnostic.FormatError());
        }

        return ParseResult.Error(globals, $"Unknown command: '{remaining[0].Value}'.");
    }

    private static readonly HashSet<string> PipingWords =
        new(StringComparer.OrdinalIgnoreCase) { "then", "pipe", "send", "and", "or" };

    private static bool ContainsPipingToken(IReadOnlyList<Token> tokens)
    {
        foreach (var t in tokens)
        {
            if (PipingWords.Contains(t.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static ParseResult? TryStagedMatch(
        IReadOnlyList<Token> remaining,
        IReadOnlyList<IChainDescriptor> registrations,
        IReadOnlyDictionary<string, string?> globals,
        bool partialsEnabled)
    {
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
                var (success, _) = TryMatch(segTokens, registrations[i], globals, i, registrations, partialsEnabled);
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
            if (PipingWords.Contains(token.Value))
            {
                result.Add((current, pendingOp));
                current = new List<Token>();
                pendingOp = token.Value;
            }
            else
            {
                current.Add(token);
            }
        }

        result.Add((current, pendingOp));
        return result;
    }

    private readonly record struct MatchCtx(
        IReadOnlyList<Token> Tokens,
        string MatchedVerb,
        bool PartialsEnabled,
        IReadOnlyList<IChainDescriptor> AllRegistrations);

    private readonly record struct MatchOutcome(
        bool Success,
        int Pos,
        int MatchedDepth,
        List<ResolvedCommand>? Resolved,
        MatchDiagnostic? Diag);

    private static MatchOutcome Fail(MatchCtx ctx, int pos, int depth, MatchDiagnostic? diag = null)
        => new(false, pos, depth, null, diag ?? new MatchDiagnostic(ctx.MatchedVerb, depth));

    private static (ParseResult? success, MatchDiagnostic? diagnostic) TryMatch(
        IReadOnlyList<Token> tokens,
        IChainDescriptor root,
        IReadOnlyDictionary<string, string?> globals,
        int registrationIndex,
        IReadOnlyList<IChainDescriptor> allRegistrations,
        bool partialsEnabled,
        bool allowExtraTokens = false)
    {
        if (tokens.Count == 0)
        {
            return (null, null);
        }

        if (!IsPlausibleHead(tokens[0].Value, root, partialsEnabled, allRegistrations))
        {
            return (null, null);
        }

        var ctx = new MatchCtx(tokens, tokens[0].Value, partialsEnabled, allRegistrations);
        var acc = new List<ResolvedCommand>();
        var outcome = MatchNode(ctx, 0, root, 0, acc, isHead: true);

        if (!outcome.Success)
        {
            return (null, outcome.Diag);
        }

        if (!allowExtraTokens && outcome.Pos != tokens.Count)
        {
            return (null, new MatchDiagnostic(ctx.MatchedVerb, outcome.MatchedDepth, HasExtraTokens: true));
        }

        var chain = new CommandChain(outcome.Resolved!);
        return (new ParseResult(chain, globals, Array.Empty<string>(), registrationIndex), null);
    }

    private static (ParseResult? success, int consumed) TryPrefixMatch(
        IReadOnlyList<Token> tokens,
        IChainDescriptor root,
        IReadOnlyDictionary<string, string?> globals,
        int registrationIndex,
        IReadOnlyList<IChainDescriptor> allRegistrations,
        bool partialsEnabled)
    {
        if (tokens.Count == 0)
        {
            return (null, 0);
        }

        if (!IsPlausibleHead(tokens[0].Value, root, partialsEnabled, allRegistrations))
        {
            return (null, 0);
        }

        var ctx = new MatchCtx(tokens, tokens[0].Value, partialsEnabled, allRegistrations);
        var acc = new List<ResolvedCommand>();
        var outcome = MatchNode(ctx, 0, root, 0, acc, isHead: true);
        if (!outcome.Success)
        {
            return (null, 0);
        }

        var chain = new CommandChain(outcome.Resolved!);
        return (new ParseResult(chain, globals, Array.Empty<string>(), registrationIndex), outcome.Pos);
    }

    private static HashSet<string> BuildVerbHeadTokens(IReadOnlyList<IChainDescriptor> registrations)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
        {
            if (reg.Kind != ChainNodeKind.Word)
            {
                continue;
            }

            set.Add(reg.Name);
            foreach (var s in reg.Synonyms)
            {
                set.Add(s);
            }
        }
        return set;
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
        bool partialsEnabled)
    {
        ParseResult? best = null;
        int bestConsumed = 0;
        int bestIndex = -1;

        for (int i = 0; i < registrations.Count; i++)
        {
            var (success, consumed) = TryPrefixMatch(sliced, registrations[i], globals, i, registrations, partialsEnabled);
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
        IReadOnlyList<Token> remaining, int pos, HashSet<string> verbHeads)
    {
        if (pos >= remaining.Count)
        {
            return (PipelineContinuation.EndOfInput, null, pos);
        }

        var next = remaining[pos].Value;
        if (PipingWords.Contains(next))
        {
            var afterOp = pos + 1;
            if (afterOp >= remaining.Count)
            {
                return (PipelineContinuation.Stop, null, pos);
            }
            return (PipelineContinuation.Explicit, next, afterOp);
        }

        if (verbHeads.Contains(next))
        {
            return (PipelineContinuation.Implicit, "then", pos);
        }

        return (PipelineContinuation.Stop, null, pos);
    }

    private static ParseResult? TryUnifiedPipelineMatch(
        IReadOnlyList<Token> remaining,
        IReadOnlyList<IChainDescriptor> registrations,
        IReadOnlyDictionary<string, string?> globals,
        bool partialsEnabled)
    {
        var verbHeads = BuildVerbHeadTokens(registrations);
        var segments = new List<PipelineSegment>();
        int pos = 0;
        string? pendingOp = null;

        while (pos < remaining.Count)
        {
            var (result, consumed, regIndex) = FindBestPrefixMatch(Slice(remaining, pos), registrations, globals, partialsEnabled);
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

    private static bool IsPlausibleHead(string token, IChainDescriptor root, bool partialsEnabled,
        IReadOnlyList<IChainDescriptor> allRegistrations)
    {
        if (root.Kind == ChainNodeKind.Word || root.Kind == ChainNodeKind.Conjunctive)
        {
            if (MatchesWord(token, root))
            {
                return true;
            }

            if (partialsEnabled && root.Kind == ChainNodeKind.Word)
            {
                var (ok, _, _) = TryPrefixHead(token, allRegistrations);
                if (ok && IsHeadPrefixOf(token, root))
                {
                    return true;
                }
            }
            return false;
        }
        return true;
    }

    private static bool IsHeadPrefixOf(string token, IChainDescriptor descriptor)
    {
        if (descriptor.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var s in descriptor.Synonyms)
        {
            if (s.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasExactHeadMatch(string token, IReadOnlyList<IChainDescriptor> allRegistrations)
    {
        foreach (var reg in allRegistrations)
        {
            if (reg.Kind != ChainNodeKind.Word)
            {
                continue;
            }

            if (string.Equals(reg.Name, token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var s in reg.Synonyms)
            {
                if (string.Equals(s, token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static (bool ok, string? chosenName, List<string>? candidates) TryPrefixHead(
        string token, IReadOnlyList<IChainDescriptor> allRegistrations)
    {
        var matches = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reg in allRegistrations)
        {
            if (reg.Kind != ChainNodeKind.Word)
            {
                continue;
            }

            if (reg.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                if (seen.Add(reg.Name))
                {
                    matches.Add(reg.Name);
                }
            }
            foreach (var s in reg.Synonyms)
            {
                if (s.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                {
                    if (seen.Add(s))
                    {
                        matches.Add(s);
                    }
                }
            }
        }

        if (matches.Count == 0)
        {
            return (false, null, null);
        }

        if (matches.Count == 1)
        {
            return (true, matches[0], null);
        }

        return (false, null, matches);
    }

    private enum MatchStep
    {
        Enter,
        InnerThenNext_AfterInner,
        Optional_AfterTake,
        Optional_AfterSkip,
        Alternation_Next,
        Repetition_AfterSeparator,
        Repetition_AfterInner,
        Word_AfterNext,
    }

    private sealed class MatchFrame
    {
        public MatchStep Step;
        public int Pos;
        public int Depth;
        public IChainDescriptor? Node;
        public List<ResolvedCommand> Acc = null!;
        public bool IsHead;
        public IChainDescriptor? Inner;
        public IChainDescriptor? Next;
        public int AltIndex;
        public MatchOutcome Best;
        public bool HaveBest;
        public int RepCount;
        public int RepMin;
        public int RepMax;
        public int CurPos;
        public int CurDepth;
        public MatchOutcome LastFailure;
        public bool HaveFailure;
    }

    private static MatchOutcome MatchNode(
        MatchCtx ctx, int pos, IChainDescriptor? node, int depth, List<ResolvedCommand> acc, bool isHead)
    {
        var stack = new Stack<MatchFrame>();
        stack.Push(new MatchFrame
        {
            Step = MatchStep.Enter,
            Pos = pos,
            Depth = depth,
            Node = node,
            Acc = acc,
            IsHead = isHead,
        });

        MatchOutcome result = default;
        bool haveResult = false;

        while (stack.Count > 0)
        {
            var frame = stack.Peek();

            switch (frame.Step)
            {
                case MatchStep.Enter:
                {
                    if (frame.Node is null)
                    {
                        result = new MatchOutcome(true, frame.Pos, frame.Depth, frame.Acc, null);
                        haveResult = true;
                        stack.Pop();
                        break;
                    }

                    switch (frame.Node.Kind)
                    {
                        case ChainNodeKind.Word:
                        {
                            var local = MatchWordNodeLocal(ctx, frame.Pos, frame.Node, frame.Depth, frame.Acc, frame.IsHead);
                            if (!local.Success)
                            {
                                result = local;
                                haveResult = true;
                                stack.Pop();
                                break;
                            }

                            frame.Step = MatchStep.Word_AfterNext;
                            PushEval(stack, frame.Node.Next, local.Pos, local.MatchedDepth, local.Resolved!, isHead: false);
                            break;
                        }
                        case ChainNodeKind.Conjunctive:
                        {
                            var local = MatchConjunctiveNodeLocal(ctx, frame.Pos, frame.Node, frame.Depth, frame.Acc);
                            if (!local.Success)
                            {
                                result = local;
                                haveResult = true;
                                stack.Pop();
                                break;
                            }

                            frame.Step = MatchStep.Word_AfterNext;
                            PushEval(stack, frame.Node.Next, local.Pos, local.MatchedDepth, local.Resolved!, isHead: false);
                            break;
                        }
                        case ChainNodeKind.Optional:
                        {
                            frame.Inner = frame.Node.OptionalInner;
                            frame.Next = frame.Node.Next;
                            if (frame.Inner is not null && frame.Pos < ctx.Tokens.Count)
                            {
                                frame.Step = MatchStep.Optional_AfterTake;
                                PushInnerThenNext(stack, frame.Inner, frame.Next, frame.Pos, frame.Depth, frame.Acc);
                            }
                            else
                            {
                                frame.Step = MatchStep.Optional_AfterSkip;
                                frame.HaveBest = false;
                                PushEval(stack, frame.Next, frame.Pos, frame.Depth, frame.Acc, isHead: false);
                            }
                            break;
                        }
                        case ChainNodeKind.Alternation:
                        {
                            frame.Next = frame.Node.Next;
                            frame.AltIndex = 0;
                            frame.HaveBest = false;
                            if (frame.Node.Alternatives.Count == 0)
                            {
                                result = Fail(ctx, frame.Pos, frame.Depth);
                                haveResult = true;
                                stack.Pop();
                                break;
                            }

                            frame.Step = MatchStep.Alternation_Next;
                            PushInnerThenNext(stack, frame.Node.Alternatives[0], frame.Next, frame.Pos, frame.Depth, frame.Acc);
                            break;
                        }
                        case ChainNodeKind.Repetition:
                        {
                            frame.CurPos = frame.Pos;
                            frame.CurDepth = frame.Depth;
                            frame.RepCount = 0;
                            var (min, max) = frame.Node.RepetitionBounds;
                            frame.RepMin = min;
                            frame.RepMax = max;
                            frame.HaveFailure = false;
                            var repResult = DriveRepetition(ctx, frame, stack);
                            if (repResult is { } repValue)
                            {
                                result = repValue;
                                haveResult = true;
                                stack.Pop();
                            }
                            break;
                        }
                        default:
                        {
                            result = Fail(ctx, frame.Pos, frame.Depth);
                            haveResult = true;
                            stack.Pop();
                            break;
                        }
                    }
                    break;
                }
                case MatchStep.InnerThenNext_AfterInner:
                {
                    var innerOutcome = result;
                    haveResult = false;
                    if (!innerOutcome.Success)
                    {
                        result = innerOutcome;
                        haveResult = true;
                        stack.Pop();
                        break;
                    }

                    stack.Pop();
                    PushEval(stack, frame.Next, innerOutcome.Pos, innerOutcome.MatchedDepth, innerOutcome.Resolved!, isHead: false);
                    break;
                }
                case MatchStep.Optional_AfterTake:
                {
                    var take = result;
                    haveResult = false;
                    if (take.Success)
                    {
                        result = take;
                        haveResult = true;
                        stack.Pop();
                        break;
                    }

                    frame.Best = take;
                    frame.HaveBest = true;
                    frame.Step = MatchStep.Optional_AfterSkip;
                    PushEval(stack, frame.Next, frame.Pos, frame.Depth, frame.Acc, isHead: false);
                    break;
                }
                case MatchStep.Optional_AfterSkip:
                {
                    var skip = result;
                    haveResult = false;
                    if (skip.Success)
                    {
                        result = skip;
                        haveResult = true;
                        stack.Pop();
                        break;
                    }

                    result = frame.HaveBest ? DeeperDiag(frame.Best, skip) : skip;
                    haveResult = true;
                    stack.Pop();
                    break;
                }
                case MatchStep.Alternation_Next:
                {
                    var outcome = result;
                    haveResult = false;
                    if (outcome.Success)
                    {
                        result = outcome;
                        haveResult = true;
                        stack.Pop();
                        break;
                    }

                    if (!frame.HaveBest || outcome.MatchedDepth > frame.Best.MatchedDepth)
                    {
                        frame.Best = outcome;
                        frame.HaveBest = true;
                    }

                    frame.AltIndex++;
                    if (frame.AltIndex < frame.Node!.Alternatives.Count)
                    {
                        PushInnerThenNext(stack, frame.Node.Alternatives[frame.AltIndex], frame.Next, frame.Pos, frame.Depth, frame.Acc);
                        break;
                    }

                    result = frame.HaveBest ? frame.Best : Fail(ctx, frame.Pos, frame.Depth);
                    haveResult = true;
                    stack.Pop();
                    break;
                }
                case MatchStep.Repetition_AfterSeparator:
                {
                    var sep = result;
                    haveResult = false;
                    if (!sep.Success)
                    {
                        frame.LastFailure = sep;
                        frame.HaveFailure = true;
                        var finished = FinishRepetition(ctx, frame, stack);
                        if (finished is { } finishedValue)
                        {
                            result = finishedValue;
                            haveResult = true;
                            stack.Pop();
                        }
                        break;
                    }

                    ReplaceAcc(frame.Acc, sep.Resolved!);
                    frame.CurDepth = sep.MatchedDepth;
                    PushEval(stack, frame.Node!.RepetitionInner, sep.Pos, frame.CurDepth, new List<ResolvedCommand>(frame.Acc), isHead: false);
                    frame.Step = MatchStep.Repetition_AfterInner;
                    break;
                }
                case MatchStep.Repetition_AfterInner:
                {
                    var inner = result;
                    haveResult = false;
                    if (!inner.Success)
                    {
                        frame.LastFailure = inner;
                        frame.HaveFailure = true;
                        var finished = FinishRepetition(ctx, frame, stack);
                        if (finished is { } finishedValue)
                        {
                            result = finishedValue;
                            haveResult = true;
                            stack.Pop();
                        }
                        break;
                    }

                    frame.CurPos = inner.Pos;
                    frame.CurDepth = inner.MatchedDepth;
                    ReplaceAcc(frame.Acc, inner.Resolved!);
                    frame.RepCount++;
                    var continued = DriveRepetition(ctx, frame, stack);
                    if (continued is { } continuedValue)
                    {
                        result = continuedValue;
                        haveResult = true;
                        stack.Pop();
                    }
                    break;
                }
                case MatchStep.Word_AfterNext:
                {
                    haveResult = true;
                    stack.Pop();
                    break;
                }
            }
        }

        return haveResult ? result : default;
    }

    private static void PushEval(
        Stack<MatchFrame> stack, IChainDescriptor? node, int pos, int depth, List<ResolvedCommand> acc, bool isHead)
    {
        stack.Push(new MatchFrame
        {
            Step = MatchStep.Enter,
            Pos = pos,
            Depth = depth,
            Node = node,
            Acc = acc,
            IsHead = isHead,
        });
    }

    private static void PushInnerThenNext(
        Stack<MatchFrame> stack, IChainDescriptor inner, IChainDescriptor? next, int pos, int depth, List<ResolvedCommand> acc)
    {
        stack.Push(new MatchFrame
        {
            Step = MatchStep.InnerThenNext_AfterInner,
            Next = next,
        });
        PushEval(stack, inner, pos, depth, new List<ResolvedCommand>(acc), isHead: false);
    }

    private static MatchOutcome? DriveRepetition(MatchCtx ctx, MatchFrame frame, Stack<MatchFrame> stack)
    {
        if (frame.RepMax != 0 && frame.RepCount >= frame.RepMax)
        {
            return FinishRepetition(ctx, frame, stack);
        }

        if (frame.RepCount > 0 && frame.Node!.RepetitionSeparator is not null)
        {
            frame.Step = MatchStep.Repetition_AfterSeparator;
            PushEval(stack, frame.Node.RepetitionSeparator, frame.CurPos, frame.CurDepth, new List<ResolvedCommand>(frame.Acc), isHead: false);
            return null;
        }

        if (frame.Node!.RepetitionInner is null)
        {
            return FinishRepetition(ctx, frame, stack);
        }

        frame.Step = MatchStep.Repetition_AfterInner;
        PushEval(stack, frame.Node.RepetitionInner, frame.CurPos, frame.CurDepth, new List<ResolvedCommand>(frame.Acc), isHead: false);
        return null;
    }

    private static MatchOutcome? FinishRepetition(MatchCtx ctx, MatchFrame frame, Stack<MatchFrame> stack)
    {
        if (frame.RepCount < frame.RepMin)
        {
            return frame.HaveFailure ? frame.LastFailure : Fail(ctx, frame.CurPos, frame.CurDepth);
        }

        frame.Step = MatchStep.Word_AfterNext;
        PushEval(stack, frame.Node!.Next, frame.CurPos, frame.CurDepth, frame.Acc, isHead: false);
        return null;
    }

    private static void ReplaceAcc(List<ResolvedCommand> acc, List<ResolvedCommand> replacement)
    {
        acc.Clear();
        acc.AddRange(replacement);
    }

    private static MatchOutcome DeeperDiag(MatchOutcome a, MatchOutcome b)
    {
        if (a.Success)
        {
            return a;
        }

        if (b.Success)
        {
            return b;
        }

        return a.MatchedDepth >= b.MatchedDepth ? a : b;
    }

    private static MatchOutcome MatchWordNodeLocal(
        MatchCtx ctx, int pos, IChainDescriptor node, int depth, List<ResolvedCommand> acc, bool isHead)
    {
        if (pos >= ctx.Tokens.Count)
        {
            var missingRequired = node.Targets.FirstOrDefault(t => t.Required);
            return missingRequired is not null
                ? Fail(ctx, pos, depth, new MatchDiagnostic(ctx.MatchedVerb, depth, MissingTarget: missingRequired.Name))
                : Fail(ctx, pos, depth);
        }

        bool allowPrefix = ctx.PartialsEnabled && isHead
                           && depth == 0;
        var (matched, matchedName, ambiguous) = MatchesWord(ctx.Tokens[pos].Value, node, allowPrefix, ctx.AllRegistrations);

        if (ambiguous is { Count: > 0 })
        {
            return Fail(ctx, pos, depth, new MatchDiagnostic(ctx.MatchedVerb, depth, AmbiguousCandidates: ambiguous));
        }

        if (!matched)
        {
            return Fail(ctx, pos, depth);
        }

        string capturedName = matchedName ?? ctx.Tokens[pos].Value;
        pos++;
        depth++;

        var targetValues = new Dictionary<string, string>();
        var (newPos, missing) = ConsumeTargets(ctx.Tokens, pos, node, targetValues);
        if (missing is not null)
        {
            return Fail(ctx, newPos, depth, new MatchDiagnostic(ctx.MatchedVerb, depth, MissingTarget: missing));
        }

        acc.Add(new ResolvedCommand(node, capturedName, targetValues));
        return new MatchOutcome(true, newPos, depth, acc, null);
    }

    private static MatchOutcome MatchConjunctiveNodeLocal(
        MatchCtx ctx, int pos, IChainDescriptor node, int depth, List<ResolvedCommand> acc)
    {
        bool literalMatch = pos < ctx.Tokens.Count
            && string.Equals(ctx.Tokens[pos].Value, node.Name, StringComparison.OrdinalIgnoreCase);

        if (!literalMatch)
        {
            if (node.ConjunctiveKind == ConjunctiveKind.StageSeparator && node.Targets.Count == 0)
            {
                acc.Add(new ResolvedCommand(node, node.Name, new Dictionary<string, string>()));
                return new MatchOutcome(true, pos, depth, acc, null);
            }

            return Fail(ctx, pos, depth, new MatchDiagnostic(ctx.MatchedVerb, depth, ExpectedConjunctive: node.Name));
        }

        pos++;
        depth++;

        var conjTargets = new Dictionary<string, string>();
        var (newPos, missing) = ConsumeTargets(ctx.Tokens, pos, node, conjTargets);
        if (missing is not null)
        {
            return Fail(ctx, newPos, depth, new MatchDiagnostic(ctx.MatchedVerb, depth, MissingTarget: missing));
        }

        acc.Add(new ResolvedCommand(node, node.Name, conjTargets));
        return new MatchOutcome(true, newPos, depth, acc, null);
    }

    private static (int newPos, string? missing) ConsumeTargets(
        IReadOnlyList<Token> tokens,
        int pos,
        IChainDescriptor node,
        Dictionary<string, string> sink)
    {
        foreach (var target in node.Targets)
        {
            if (pos >= tokens.Count)
            {
                if (target.Required)
                {
                    return (pos, target.Name);
                }

                continue;
            }

            if (node.Next is not null && IsChainNodeMatch(tokens[pos].Value, node.Next))
            {
                if (target.Required)
                {
                    return (pos, target.Name);
                }

                continue;
            }

            if (target.Variadic)
            {
                pos = ConsumeVariadicTokens(tokens, pos, node, target, sink);
            }
            else
            {
                sink[target.Name] = GetTokenValue(tokens[pos]);
                pos++;
            }
        }

        return (pos, null);
    }

    private static int ConsumeVariadicTokens(
        IReadOnlyList<Token> tokens,
        int pos,
        IChainDescriptor node,
        ITargetDescriptor target,
        Dictionary<string, string> sink)
    {
        var values = new List<string>();
        values.Add(GetTokenValue(tokens[pos]));
        pos++;

        while (pos < tokens.Count)
        {
            if (tokens[pos].Kind == TokenKind.CommaSeparator)
            {
                pos++;
                if (pos < tokens.Count)
                {
                    values.Add(GetTokenValue(tokens[pos]));
                    pos++;
                }
            }
            else if (node.Next is not null && IsChainNodeMatch(tokens[pos].Value, node.Next))
            {
                break;
            }
            else
            {
                values.Add(GetTokenValue(tokens[pos]));
                pos++;
            }
        }

        sink[target.Name] = string.Join(",", values);
        return pos;
    }

    private static (bool matched, string? matchedName, IReadOnlyList<string>? ambiguous) MatchesWord(
        string token,
        IChainDescriptor descriptor,
        bool allowPrefix,
        IReadOnlyList<IChainDescriptor> allRegistrations)
    {
        if (TryDirectWordMatch(token, descriptor) is { } direct)
        {
            return (true, direct, null);
        }

        if (!allowPrefix)
        {
            return (false, null, null);
        }

        var (ok, chosen, candidates) = TryPrefixHead(token, allRegistrations);
        if (candidates is { Count: > 0 })
        {
            return (false, null, candidates);
        }

        if (!ok || chosen is null)
        {
            return (false, null, null);
        }

        return TryDirectWordMatch(chosen, descriptor) is { } indirect
            ? (true, indirect, null)
            : (false, null, null);
    }

    private static string? TryDirectWordMatch(string token, IChainDescriptor descriptor)
    {
        if (string.Equals(token, descriptor.Name, StringComparison.OrdinalIgnoreCase))
        {
            return descriptor.Name;
        }

        foreach (var synonym in descriptor.Synonyms)
        {
            if (string.Equals(token, synonym, StringComparison.OrdinalIgnoreCase))
            {
                return synonym;
            }
        }

        return null;
    }

    private static bool MatchesWord(string token, IChainDescriptor descriptor)
        => TryDirectWordMatch(token, descriptor) is not null;

    private static bool IsChainNodeMatch(string token, IChainDescriptor descriptor)
    {
        if (descriptor.Kind == ChainNodeKind.Conjunctive)
        {
            return string.Equals(token, descriptor.Name, StringComparison.OrdinalIgnoreCase);
        }

        if (descriptor.Kind == ChainNodeKind.Optional)
        {
            if (descriptor.OptionalInner is not null && IsChainNodeMatch(token, descriptor.OptionalInner))
            {
                return true;
            }

            return descriptor.Next is not null && IsChainNodeMatch(token, descriptor.Next);
        }

        if (descriptor.Kind == ChainNodeKind.Alternation)
        {
            foreach (var alt in descriptor.Alternatives)
            {
                if (IsChainNodeMatch(token, alt))
                {
                    return true;
                }
            }

            return false;
        }

        if (descriptor.Kind == ChainNodeKind.Repetition)
        {
            if (descriptor.RepetitionInner is not null && IsChainNodeMatch(token, descriptor.RepetitionInner))
            {
                return true;
            }

            return descriptor.Next is not null && IsChainNodeMatch(token, descriptor.Next);
        }

        return MatchesWord(token, descriptor);
    }

    private static string GetTokenValue(Token token) => token.Value;
}
