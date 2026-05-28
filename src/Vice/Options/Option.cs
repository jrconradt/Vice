namespace Vice.Options;

public abstract record OptionBase : GlobalOption
{
    protected OptionBase(string name, string description) : base(name, description) { }
}

public sealed record Option<T> : OptionBase
{
    public Option(string name, string description) : base(name, description) { }

    public T? Default { get; init; }

    public Func<string, T>? Parser { get; init; }

    public override bool TakesValue { get; init; } = true;

    public T? ParseOrDefault(string? raw)
    {
        if (raw is null)
        {
            return Default;
        }

        if (Parser is null)
        {
            throw new InvalidOperationException($"Option '--{Name}' has no Parser set.");
        }

        return Parser(raw);
    }
}

public static class Option
{
    public static Option<bool> Flag(string name, string description, params string[] aliases) =>
        new(name, description)
        {
            TakesValue = false,
            Default = false,
            Parser = static _ => true,
            Aliases = aliases,
        };
}
