using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record NoPagerOption()
    : FlagOption("no-pager", "Disable PAGER wrapping for long output");
