using Vice.Composition;
using Vice.Options;

namespace Vice.Files;

[ViceOption]
public sealed record IncludeHiddenOption()
    : FlagOption("include-hidden", "search: include dotfiles and hidden entries");
