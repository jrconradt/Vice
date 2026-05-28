namespace Vice.Parser;

public sealed record MatchDiagnostic(
    string MatchedVerb,
    int MatchedDepth,
    string? MissingTarget = null,
    string? ExpectedConjunctive = null,
    bool HasExtraTokens = false,
    IReadOnlyList<string>? AmbiguousCandidates = null)
{
    public string FormatError()
    {
        if (AmbiguousCandidates is { Count: > 0 })
        {
            return $"Command '{MatchedVerb}': ambiguous prefix, candidates: {string.Join(", ", AmbiguousCandidates)}.";
        }

        if (MissingTarget is not null)
        {
            return $"Command '{MatchedVerb}': missing required value for '{MissingTarget}'.";
        }

        if (ExpectedConjunctive is not null)
        {
            return $"Command '{MatchedVerb}': expected '{ExpectedConjunctive}' keyword.";
        }

        if (HasExtraTokens)
        {
            return $"Command '{MatchedVerb}': unexpected extra arguments.";
        }

        return $"Command '{MatchedVerb}': incomplete command.";
    }
}
