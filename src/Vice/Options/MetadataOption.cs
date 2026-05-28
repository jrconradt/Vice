using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record MetadataOption()
    : ValueBearingOption("metadata", "Per-call metadata as JSON");
