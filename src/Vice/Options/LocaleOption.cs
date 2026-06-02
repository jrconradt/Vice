namespace Vice.Options;

[ViceOption]
public sealed record LocaleOption()
    : ValueBearingOption("locale", "BCP-47 locale tag for output formatting (e.g. en-US, de-DE)");
