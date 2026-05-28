using Vice.Composition;

namespace Vice.Options;

[ViceOption]
public sealed record FormatOption()
    : ValueBearingOption("format",
                         "Output or content format (text|hex|json|...)",
                         new[] { "auto", "text", "hex", "json", "jsonl", "ndjson" },
                         "auto");
