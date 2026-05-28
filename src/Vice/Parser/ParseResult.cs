namespace Vice.Parser;

public sealed class ParseResult
{
    public CommandChain? Chain { get; }
    public IReadOnlyList<PipelineSegment>? Segments { get; }
    public IReadOnlyDictionary<string, string?> GlobalOptions { get; }
    public IReadOnlyList<string> Errors { get; }
    public int MatchedRegistrationIndex { get; }
    public MatchDiagnostic? BestMatch { get; }

    public bool Success => Errors.Count == 0 && (Chain is not null || Segments is not null);

    public ParseResult(
    CommandChain? chain,
    IReadOnlyDictionary<string, string?> globalOptions,
    IReadOnlyList<string> errors,
    int matchedRegistrationIndex = -1,
    MatchDiagnostic? bestMatch = null)
    {
        Chain = chain;
        GlobalOptions = globalOptions;
        Errors = errors;
        MatchedRegistrationIndex = matchedRegistrationIndex;
        BestMatch = bestMatch;
    }

    public ParseResult(
    IReadOnlyList<PipelineSegment> segments,
    IReadOnlyDictionary<string, string?> globalOptions)
    {
        Segments = segments;
        GlobalOptions = globalOptions;
        Errors = Array.Empty<string>();
        MatchedRegistrationIndex = -1;
    }

    public static ParseResult Error(IReadOnlyDictionary<string, string?> globalOptions, params string[] errors)
        => new(null, globalOptions, errors);

    public static ParseResult Error(IReadOnlyDictionary<string, string?> globalOptions, MatchDiagnostic diagnostic, params string[] errors)
        => new(null, globalOptions, errors, bestMatch: diagnostic);
}
