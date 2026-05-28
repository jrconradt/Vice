using Vice.Options;

namespace Vice.Tests;

internal static class TestOptions
{
    public static readonly IReadOnlyList<GlobalOption> All = new GlobalOption[]
    {
        new HelpOption(),
        new VersionOption(),
        new VerboseOption(),
        new QuietOption(),
        new NoStatusOption(),
        new NoColorOption(),
        new NoPagerOption(),
        new DryRunOption(),
        new NonInteractiveOption(),
        new TimeoutOption(),
        new FormatOption(),
        new EncodingOption(),
        new MetadataOption(),
        new LimitOption(),
        new OffsetOption(),
        new PageOption(),
        new DepthOption(),
        new ChunkSizeOption(),
        new ColorOption(),
        new LocaleOption(),
    };
}
