using Vice.Composition;
using Vice.Options;

namespace Vice.Net.Options;

[ViceOption]
public sealed record InsecureOption()
    : FlagOption("insecure", "gRPC: skip TLS certificate validation");
