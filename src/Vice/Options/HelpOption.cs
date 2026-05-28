using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record HelpOption()
    : FlagOption("help", "Show help");
