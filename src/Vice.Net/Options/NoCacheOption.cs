using Vice.Composition;
using Vice.Options;

namespace Vice.Net.Options;

[ViceOption]
public sealed record NoCacheOption()
    : FlagOption("no-cache", "Bypass the on-disk research cache for this call");
