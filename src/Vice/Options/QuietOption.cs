using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record QuietOption()
    : FlagOption("quiet", "Suppress non-error output");
