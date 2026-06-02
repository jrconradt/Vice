namespace Vice.Completions;

internal static class CompletionEmit
{
    public static string SanitizeIdentifier(string name)
        => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));

    public static string FunctionName(string appName)
        => "_" + SanitizeIdentifier(appName);

    public static string AliasPrefix(string alias)
        => alias.Length == 1 ? "-" : "--";

    public static IReadOnlyList<string> TransitionLines(IReadOnlyList<CompletionTransition> transitions,
                                                        Func<string, string> escape)
    {
        var lines = new List<string>(transitions.Count);
        foreach (var t in transitions)
        {
            var parent = escape(t.ParentStateId);
            var child = escape(t.ChildStateId);
            var pattern = string.Join("|", t.Tokens.Select(tok => $"\"{parent}:{escape(tok)}\""));
            var setSkip = t.TargetCount > 0 ? $" skip={t.TargetCount};" : "";
            lines.Add($"            {pattern}) state=\"{child}\";{setSkip} ;;");
        }

        return lines;
    }
}
