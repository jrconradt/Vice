namespace Vice.Completions;

internal sealed class CompletionNode
{
    public string Token { get; init; } = "";
    public List<string> Synonyms { get; } = new();
    public int TargetCount { get; set; }
    public bool IsTerminal { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, CompletionNode> Children { get; } = new(StringComparer.Ordinal);
    public string StateId { get; set; } = "root";

    public IEnumerable<string> AllTokens()
        => Synonyms.Count == 0 ? new[] { Token } : new[] { Token }.Concat(Synonyms);
}
