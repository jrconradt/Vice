namespace Vice.Options;

public record ValueBearingOption(string Name,
                                 string Description,
                                 IEnumerable<string>? AcceptedValues = null,
                                 string? Default = null)
    : GlobalOption(Name, Description)
{
    public override bool TakesValue { get; init; } = true;
}
