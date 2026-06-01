using Vice.Options;

namespace Vice.Files;

[ViceOption]
public sealed record RegexOption()
    : FlagOption("regex", "search: treat name/path patterns as .NET regex instead of glob");
