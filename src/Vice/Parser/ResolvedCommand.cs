namespace Vice.Parser;

public sealed class ResolvedCommand
{
    public IChainDescriptor Descriptor { get; }
    public IReadOnlyDictionary<string, string> TargetValues { get; }
    public string MatchedName { get; }

    public ResolvedCommand(IChainDescriptor descriptor, string matchedName, IReadOnlyDictionary<string, string> targetValues)
    {
        Descriptor = descriptor;
        MatchedName = matchedName;
        TargetValues = targetValues;
    }
}
