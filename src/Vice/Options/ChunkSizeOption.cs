namespace Vice.Options;

[ViceOption]
public sealed record ChunkSizeOption()
    : ValueBearingOption("chunk-size", "Byte-stream chunk size in bytes");
