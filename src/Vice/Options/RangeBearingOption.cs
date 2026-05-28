namespace Vice.Options;

public record RangeBearingOption(string Name,
                                 string Description,
                                 Func<int, bool> Validate,
                                 string? Default = null)
    : GlobalOption(Name, Description)
{
    public override bool TakesValue { get; init; } = true;
}
