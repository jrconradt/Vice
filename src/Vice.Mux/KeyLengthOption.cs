using Vice.Composition;
using Vice.Options;

namespace Vice.Mux;

[ViceOption]
public sealed record KeyLengthOption()
    : ValueBearingOption("key-length", "sticky-key: byte length of key slice (default 4)");
