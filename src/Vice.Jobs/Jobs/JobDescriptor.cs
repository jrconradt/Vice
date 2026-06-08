namespace Vice.Jobs;

public sealed record JobDescriptor(
    JobKind Kind,
    string Label,
    IReadOnlyDictionary<string, string?> Options);
