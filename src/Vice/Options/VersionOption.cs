namespace Vice.Options;

[ViceOption]
public sealed record VersionOption()
    : FlagOption("version", "Show version");
