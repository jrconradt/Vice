using Vice.Options;

namespace Vice.Mux;

public sealed record SeedOption()
    : ValueBearingOption("seed", "Hash/random seed (uint64)");
