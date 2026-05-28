using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record LimitOption()
    : ValueBearingOption("limit", "Maximum results to return");
