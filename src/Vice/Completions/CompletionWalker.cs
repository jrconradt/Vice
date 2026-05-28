namespace Vice.Completions;

internal readonly record struct CompletionTransition(
    string ParentStateId,
    IReadOnlyList<string> Tokens,
    string ChildStateId,
    int TargetCount);

internal readonly record struct CompletionCandidate(
    string Token,
    string? Description,
    IReadOnlyList<string> Synonyms);

internal readonly record struct CompletionSuggestion(
    string StateId,
    IReadOnlyList<string> AllTokens,
    IReadOnlyList<CompletionCandidate> Candidates);

internal static class CompletionWalker
{
    public static (IReadOnlyList<CompletionTransition> Transitions, IReadOnlyList<CompletionSuggestion> Suggestions) Walk(CompletionNode root)
    {
        var transitions = new List<CompletionTransition>();
        var suggestions = new List<CompletionSuggestion>();
        EmitSuggestions(root, suggestions);
        EmitTransitions(root, transitions);
        return (transitions, suggestions);
    }

    private static void EmitTransitions(CompletionNode node, List<CompletionTransition> sink)
    {
        foreach (var child in node.Children.Values)
        {
            sink.Add(new CompletionTransition(
                node.StateId,
                child.AllTokens().ToArray(),
                child.StateId,
                child.TargetCount));
            EmitTransitions(child, sink);
        }
    }

    private static void EmitSuggestions(CompletionNode node, List<CompletionSuggestion> sink)
    {
        if (node.Children.Count > 0)
        {
            var candidates = new List<CompletionCandidate>(node.Children.Count);
            var allTokens = new List<string>();
            foreach (var child in node.Children.Values)
            {
                candidates.Add(new CompletionCandidate(
                    child.Token,
                    child.Description,
                    child.Synonyms.ToArray()));
                allTokens.Add(child.Token);
                foreach (var syn in child.Synonyms)
                {
                    allTokens.Add(syn);
                }
            }
            sink.Add(new CompletionSuggestion(node.StateId, allTokens, candidates));
        }
        foreach (var child in node.Children.Values)
        {
            EmitSuggestions(child, sink);
        }
    }
}
