using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record PageOption()
    : ValueBearingOption("page", "1-based page number (alternative to offset)");
