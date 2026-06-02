namespace Vice.Options;

[ViceOption]
public sealed record OffsetOption()
    : ValueBearingOption("offset", "Skip this many results");
