namespace Vice.Options;

[ViceOption]
public sealed record NoColorOption()
    : FlagOption("no-color", "Disable ANSI color output");
