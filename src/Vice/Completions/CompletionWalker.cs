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
        var stack = new Stack<CompletionNode>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var ordered = current.Children.Values.OrderBy(c => c.Token, StringComparer.Ordinal).ToArray();
            foreach (var child in ordered)
            {
                sink.Add(new CompletionTransition(
                    current.StateId,
                    child.AllTokens().ToArray(),
                    child.StateId,
                    child.TargetCount));
            }
            for (var i = ordered.Length - 1; i >= 0; i--)
            {
                stack.Push(ordered[i]);
            }
        }
    }

    private static void EmitSuggestions(CompletionNode node, List<CompletionSuggestion> sink)
    {
        var stack = new Stack<CompletionNode>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var ordered = current.Children.Values.OrderBy(c => c.Token, StringComparer.Ordinal).ToArray();
            if (ordered.Length > 0)
            {
                var candidates = new List<CompletionCandidate>(ordered.Length);
                var allTokens = new List<string>();
                foreach (var child in ordered)
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
                sink.Add(new CompletionSuggestion(current.StateId, allTokens, candidates));
            }
            for (var i = ordered.Length - 1; i >= 0; i--)
            {
                stack.Push(ordered[i]);
            }
        }
    }
}
