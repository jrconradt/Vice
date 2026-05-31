using Vice.Composition;
using Vice.Options;

namespace Vice.Mux;

[ViceOption]
public sealed record KeyOffsetOption()
    : ValueBearingOption("key-offset", "sticky-key: byte offset into chunk for key slice (default 0)");
