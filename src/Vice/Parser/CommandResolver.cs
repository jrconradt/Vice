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

    private static MatchOutcome MatchNode(
        MatchCtx ctx, int pos, IChainDescriptor? node, int depth, List<ResolvedCommand> acc, bool isHead)
        => node is null
            ? new MatchOutcome(true, pos, depth, acc, null)
            : node.Kind switch
            {
                ChainNodeKind.Word => MatchWordNode(ctx, pos, node, depth, acc, isHead),
                ChainNodeKind.Conjunctive => MatchConjunctiveNode(ctx, pos, node, depth, acc),
                ChainNodeKind.Optional => MatchOptionalNode(ctx, pos, node, depth, acc),
                ChainNodeKind.Alternation => MatchAlternationNode(ctx, pos, node, depth, acc),
                ChainNodeKind.Repetition => MatchRepetitionNode(ctx, pos, node, depth, acc),
                _ => Fail(ctx, pos, depth),
            };

    private static MatchOutcome TryInnerThenNext(
        MatchCtx ctx, int pos, IChainDescriptor inner, IChainDescriptor? next, int depth, List<ResolvedCommand> acc)
    {
        var snapshot = new List<ResolvedCommand>(acc);
        var innerOutcome = MatchNode(ctx, pos, inner, depth, snapshot, isHead: false);
        if (!innerOutcome.Success)
        {
            return innerOutcome;
        }

        return MatchNode(ctx, innerOutcome.Pos, next, innerOutcome.MatchedDepth, innerOutcome.Resolved!, isHead: false);
    }

    private static MatchOutcome MatchOptionalNode(
        MatchCtx ctx, int pos, IChainDescriptor node, int depth, List<ResolvedCommand> acc)
    {
        MatchOutcome best = default;
        bool haveBest = false;

        if (node.OptionalInner is not null && pos < ctx.Tokens.Count)
        {
            var take = TryInnerThenNext(ctx, pos, node.OptionalInner, node.Next, depth, acc);
            if (take.Success)
            {
                return take;
            }

            best = take;
            haveBest = true;
        }

        var skip = MatchNode(ctx, pos, node.Next, depth, acc, isHead: false);
        if (skip.Success)
        {
            return skip;
        }

        return haveBest ? DeeperDiag(best, skip) : skip;
    }

    private static MatchOutcome MatchAlternationNode(
        MatchCtx ctx, int pos, IChainDescriptor node, int depth, List<ResolvedCommand> acc)
    {
        MatchOutcome best = default;
        bool haveBest = false;

        foreach (var alt in node.Alternatives)
        {
            var outcome = TryInnerThenNext(ctx, pos, alt, node.Next, depth, acc);
            if (outcome.Success)
            {
                return outcome;
            }

            if (!haveBest || outcome.MatchedDepth > best.MatchedDepth)
            {
                best = outcome;
                haveBest = true;
            }
        }

        return haveBest ? best : Fail(ctx, pos, depth);
    }

    private static MatchOutcome MatchRepetitionNode(
        MatchCtx ctx, int pos, IChainDescriptor node, int depth, List<ResolvedCommand> acc)
    {
        int curPos = pos;
        int curDepth = depth;
        int count = 0;
        var (min, max) = node.RepetitionBounds;
        MatchOutcome lastFailure = default;
        bool haveFailure = false;

        while (max == 0 || count < max)
        {
            int attemptPos = curPos;

            if (count > 0 && node.RepetitionSeparator is not null)
            {
                var sep = MatchNode(ctx, attemptPos, node.RepetitionSeparator, curDepth, new List<ResolvedCommand>(acc), isHead: false);
                if (!sep.Success)
                {
                    lastFailure = sep;
                    haveFailure = true;
                    break;
                }
                attemptPos = sep.Pos;
                ReplaceAcc(acc, sep.Resolved!);
                curDepth = sep.MatchedDepth;
            }

            if (node.RepetitionInner is null)
            {
                break;
            }

            var inner = MatchNode(ctx, attemptPos, node.RepetitionInner, curDepth, new List<ResolvedCommand>(acc), isHead: false);
            if (!inner.Success)
            {
                lastFailure = inner;
                haveFailure = true;
                break;
            }

            curPos = inner.Pos;
            curDepth = inner.MatchedDepth;
            ReplaceAcc(acc, inner.Resolved!);
            count++;
        }

        if (count < min)
        {
            return haveFailure ? lastFailure : Fail(ctx, curPos, curDepth);
        }

        return MatchNode(ctx, curPos, node.Next, curDepth, acc, isHead: false);
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

    private static MatchOutcome MatchWordNode(
        MatchCtx ctx, int pos, IChainDescriptor node, int depth, List<ResolvedCommand> acc, bool isHead)
    {
        if (pos >= ctx.Tokens.Count)
        {
            var missingRequired = node.Targets.FirstOrDefault(t => t.Required);
            return missingRequired is not null
                ? Fail(ctx, pos, depth, new MatchDiagnostic(ctx.MatchedVerb, depth, MissingTarget: missingRequired.Name))
                : Fail(ctx, pos, depth);
        }

        bool allowPrefix = ctx.PartialsEnabled && isHead && depth == 0;
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
        return MatchNode(ctx, newPos, node.Next, depth, acc, isHead: false);
    }

    private static MatchOutcome MatchConjunctiveNode(
        MatchCtx ctx, int pos, IChainDescriptor node, int depth, List<ResolvedCommand> acc)
    {
        bool literalMatch = pos < ctx.Tokens.Count
            && string.Equals(ctx.Tokens[pos].Value, node.Name, StringComparison.OrdinalIgnoreCase);

        if (!literalMatch)
        {
            if (node.ConjunctiveKind == ConjunctiveKind.Piping && node.Targets.Count == 0)
            {
                acc.Add(new ResolvedCommand(node, node.Name, new Dictionary<string, string>()));
                return MatchNode(ctx, pos, node.Next, depth, acc, isHead: false);
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
        return MatchNode(ctx, newPos, node.Next, depth, acc, isHead: false);
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
