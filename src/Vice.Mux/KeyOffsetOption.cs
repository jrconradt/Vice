using Vice.Options;

namespace Vice.Mux;

public sealed record KeyOffsetOption()
    : ValueBearingOption("key-offset", "sticky-key: byte offset into chunk for key slice (default 0)");
