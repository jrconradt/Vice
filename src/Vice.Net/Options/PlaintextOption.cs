using Vice.Composition;
using Vice.Options;

namespace Vice.Net.Options;

[ViceOption]
public sealed record PlaintextOption()
    : FlagOption("plaintext", "gRPC: use http:// instead of https://");
