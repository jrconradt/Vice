namespace Vice.Options;

public abstract record GlobalOption(string Name, string Description)
{
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    public bool Required { get; init; }

    public virtual bool TakesValue { get; init; }
}
