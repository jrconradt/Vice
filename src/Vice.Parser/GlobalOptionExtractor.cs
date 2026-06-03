namespace Vice.Parser;

public static class GlobalOptionExtractor
{
    private static readonly HashSet<string> KnownGlobalOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "help", "version", "verbose"
    };

    private static readonly IReadOnlySet<string> ValueBearingOptions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "timeout", "format", "encoding", "metadata", "limit", "offset", "page",
            "filter", "pattern"
        };

    private enum ValueMode
    {
        NoValue,
        RequireValue,
        OptionalValue
    }

    private readonly record struct OptionDecision(
        string Key,
        ValueMode Mode,
        IReadOnlyList<string>? AcceptedValues,
        Func<int, bool>? RangeValidator,
        string? UnknownError);

    public static (IReadOnlyList<Token> remaining, IReadOnlyDictionary<string, string?> globals) Extract(IReadOnlyList<Token> tokens)
    {
        var (remaining, globals, _) = Extract(tokens, ValueBearingOptions);
        return (remaining, globals);
    }

    public static (IReadOnlyList<Token> remaining, IReadOnlyDictionary<string, string?> globals, IReadOnlyList<string> errors) Extract(
        IReadOnlyList<Token> tokens,
        OptionRegistry registry)
    {
        var (remaining, globals, errors) = Walk(tokens, token => ResolveFromRegistry(registry, token));

        foreach (var meta in registry.All)
        {
            if (meta.Required && !globals.ContainsKey(meta.CanonicalName))
            {
                errors.Add($"Required option '--{meta.CanonicalName}' was not provided.");
            }
        }

        return (remaining, globals, errors);
    }

    public static (IReadOnlyList<Token> remaining, IReadOnlyDictionary<string, string?> globals, IReadOnlyList<string> errors) Extract(
        IReadOnlyList<Token> tokens,
        IReadOnlySet<string>? valueBearingOptions,
        IReadOnlySet<string>? knownFlags = null)
    {
        var (remaining, globals, errors) = Walk(tokens, token => ResolveFromSets(valueBearingOptions, knownFlags, token));
        return (remaining, globals, errors);
    }

    private static (List<Token> remaining, Dictionary<string, string?> globals, List<string> errors) Walk(
        IReadOnlyList<Token> tokens,
        Func<Token, OptionDecision> resolve)
    {
        var remaining = new List<Token>();
        var globals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind != TokenKind.GlobalOption)
            {
                remaining.Add(token);
                continue;
            }

            var decision = resolve(token);
            if (decision.UnknownError is not null)
            {
                errors.Add(decision.UnknownError);
            }

            if (decision.Mode == ValueMode.NoValue)
            {
                globals[decision.Key] = null;
                continue;
            }

            bool hasValue = i + 1 < tokens.Count
                && (tokens[i + 1].Kind == TokenKind.Word
                    || tokens[i + 1].Kind == TokenKind.Quoted);
            if (hasValue)
            {
                var raw = tokens[i + 1].Value;
                globals[decision.Key] = raw;
                i++;
                if (decision.AcceptedValues is not null
                    && !decision.AcceptedValues.Contains(raw, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Option '--{decision.Key}' value '{raw}' is not one of: {string.Join(", ", decision.AcceptedValues)}.");
                }

                if (decision.RangeValidator is not null
                    && (!int.TryParse(raw, out var parsed) || !decision.RangeValidator(parsed)))
                {
                    errors.Add($"Option '--{decision.Key}' value '{raw}' is out of range.");
                }
            }
            else if (decision.Mode == ValueMode.RequireValue)
            {
                errors.Add($"Option '--{decision.Key}' requires a value.");
                globals[decision.Key] = null;
            }
            else
            {
                globals[decision.Key] = null;
            }
        }

        return (remaining, globals, errors);
    }

    private static OptionDecision ResolveFromRegistry(OptionRegistry registry, Token token)
    {
        if (!registry.TryResolve(token.Value, out var meta))
        {
            var unknownError = KnownGlobalOptions.Contains(token.Value)
                ? null
                : $"Unknown option '--{token.Value}'.";
            return new OptionDecision(token.Value,
                                      ValueMode.NoValue,
                                      null,
                                      null,
                                      unknownError);
        }

        if (meta.TakesValue)
        {
            return new OptionDecision(meta.CanonicalName,
                                      ValueMode.RequireValue,
                                      meta.AcceptedValues,
                                      meta.RangeValidator,
                                      null);
        }

        return new OptionDecision(meta.CanonicalName,
                                  ValueMode.NoValue,
                                  null,
                                  null,
                                  null);
    }

    private static OptionDecision ResolveFromSets(
        IReadOnlySet<string>? valueBearingOptions,
        IReadOnlySet<string>? knownFlags,
        Token token)
    {
        if (KnownGlobalOptions.Contains(token.Value))
        {
            return new OptionDecision(token.Value,
                                      ValueMode.NoValue,
                                      null,
                                      null,
                                      null);
        }

        bool needsValue = valueBearingOptions?.Contains(token.Value) ?? false;
        bool isFlag = knownFlags?.Contains(token.Value) ?? false;
        if (isFlag)
        {
            return new OptionDecision(token.Value,
                                      ValueMode.NoValue,
                                      null,
                                      null,
                                      null);
        }

        var mode = needsValue ? ValueMode.RequireValue : ValueMode.OptionalValue;
        return new OptionDecision(token.Value,
                                  mode,
                                  null,
                                  null,
                                  null);
    }
}
