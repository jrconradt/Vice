namespace Vice.Completions;

internal sealed record GlobalOptionEntry(string Name, string Description, bool ValueBearing, IReadOnlyList<string> Aliases);
