namespace Vice.Parser;

public static class CommandResolver
{
    public static ParseResult Resolve(string[] args, IReadOnlyList<IChainDescriptor> registrations,
        IReadOnlySet<string>? valueBearingOptions = null,
        IReadOnlySet<string>? knownFlags = null,
        bool partialsEnabled = true,
        bool implicitPipelinesEnabled = true)
    {
        var tokens = Lexer.Tokenize(args);
        return ResolveTokens(tokens, registrations, valueBearingOptions, knownFlags, partialsEnabled, implicitPipelinesEnabled);
    }

    public static ParseResult Resolve(string input, IReadOnlyList<IChainDescriptor> registrations,
        IReadOnlySet<string>? valueBearingOptions = null,
        IReadOnlySet<string>? knownFlags = null,
        bool partialsEnabled = true,
        bool implicitPipelinesEnabled = true)
    {
        var tokens = Lexer.Tokenize(input);
        return ResolveTokens(tokens, registrations, valueBearingOptions, knownFlags, partialsEnabled, implicitPipelinesEnabled);
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

        var headIndex = HeadIndex.Build(registrations);

        MatchDiagnostic? ambiguousDiagnostic = null;
        if (partialsEnabled)
        {
            var (_, _, prefixCandidates) = headIndex.TryPrefixHead(remaining[0].Value);
            if (prefixCandidates is { Count: > 0 } &&
                !headIndex.HasExactHeadMatch(remaining[0].Value))
            {
                ambiguousDiagnostic = new MatchDiagnostic(remaining[0].Value, 0, AmbiguousCandidates: prefixCandidates);
            }
        }

        MatchDiagnostic? bestDiagnostic = null;

        for (int i = 0; i < registrations.Count; i++)
        {
            var (success, diagnostic) = ChainMatcher.TryMatch(remaining, registrations[i], globals, i, headIndex, partialsEnabled);
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
            var unified = PipelineSplitter.TryUnifiedPipelineMatch(remaining, registrations, globals, partialsEnabled);
            if (unified is not null)
            {
                return unified;
            }
        }

        if (PipelineSplitter.ContainsPipingToken(remaining))
        {
            var staged = PipelineSplitter.TryStagedMatch(remaining, registrations, globals, partialsEnabled);
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

    public static readonly IReadOnlySet<string> PipingWords =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "then", "pipe", "send", "and", "or" };
}
