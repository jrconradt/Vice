namespace Vice.Options;

[ViceOption]
public sealed record VerboseOption()
    : FlagOption("verbose", "Verbose output");
