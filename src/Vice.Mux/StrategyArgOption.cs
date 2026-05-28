using Vice.Options;

namespace Vice.Mux;

public sealed record StrategyArgOption()
    : ValueBearingOption("strategy-arg", "Strategy-specific configuration (e.g. weighted: '3:1' (colon-separated; comma is reserved by the parser))");
