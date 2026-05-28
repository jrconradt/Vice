using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record DepthOption()
    : ValueBearingOption("depth", "Maximum recursion depth");
