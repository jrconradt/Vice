using Vice.Composition;
using Vice.Options;

namespace Vice.Mux;

[ViceOption]
public sealed record SeedOption()
    : ValueBearingOption("seed", "Hash/random seed (uint64)");
