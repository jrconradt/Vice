using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record NoStatusOption()
    : FlagOption("no-status", "Disable status spinner");
