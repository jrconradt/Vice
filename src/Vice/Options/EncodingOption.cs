using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record EncodingOption()
    : ValueBearingOption("encoding",
                         "Text encoding (utf8|ascii)",
                         new[] { "utf8", "ascii" },
                         "utf8");
