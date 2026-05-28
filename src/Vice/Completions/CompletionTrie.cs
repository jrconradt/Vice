namespace Vice.Completions;

internal sealed record CompletionTrie(
    string AppName,
    CompletionNode Root,
    IReadOnlyList<GlobalOptionEntry> GlobalOptions);
