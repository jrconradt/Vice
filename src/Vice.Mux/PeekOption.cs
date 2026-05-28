using Vice.Options;

namespace Vice.Mux;

public sealed record PeekOption()
    : ValueBearingOption("peek", "inspect: emit first N bytes of each chunk as hex on stderr");
