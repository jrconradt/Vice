using Vice.Parser;

namespace Vice.Help;

public static class HelpFormatter
{
    internal static readonly IReadOnlyDictionary<string, string> OperatorDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["then"] = "Run next stage with previous output",
            ["send"] = "Send previous output to next stage",
            ["and"] = "Run both stages, combine output",
            ["pipe"] = "Pipe output to next stage",
            ["or"] = "Run next stage only if previous failed"
        };

    internal static bool HasPipelineStages(IChainDescriptor root)
        => root.HasPipelineStages();

    internal static IReadOnlyList<PipelineStageInfo> FormatPipelineStages(IChainDescriptor root)
        => root.GetPipelineStages();

    public static string FormatChain(IChainDescriptor root)
        => root.Render();

    public static IReadOnlyList<TargetEntry> FormatTargetList(IChainDescriptor root)
        => root.GetTargets();
}
