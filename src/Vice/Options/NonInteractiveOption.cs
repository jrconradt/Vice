using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record NonInteractiveOption()
    : FlagOption("non-interactive", "Refuse to prompt; fail fast on missing input");
