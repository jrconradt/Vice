using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record ColorOption()
    : ValueBearingOption("color",
                         "Force or suppress color output (auto|always|never)",
                         new[] { "auto", "always", "never" },
                         "auto");
