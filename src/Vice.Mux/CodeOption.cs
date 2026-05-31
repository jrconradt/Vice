using Vice.Composition;
using Vice.Options;

namespace Vice.Mux;

[ViceOption]
public sealed record CodeOption()
    : ValueBearingOption("code", "route: upstream exit code to match conditions against (default 0)");
