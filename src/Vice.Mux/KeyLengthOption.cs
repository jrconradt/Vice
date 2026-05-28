using Vice.Options;

namespace Vice.Mux;

public sealed record KeyLengthOption()
    : ValueBearingOption("key-length", "sticky-key: byte length of key slice (default 4)");
