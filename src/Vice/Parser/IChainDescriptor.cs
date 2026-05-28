namespace Vice.Parser;

public interface IChainDescriptor
{
    string Name { get; }
    ChainNodeKind Kind { get; }
    IReadOnlyList<string> Synonyms { get; }
    IReadOnlyList<ITargetDescriptor> Targets { get; }
    IChainDescriptor? Next { get; }
    ConjunctiveKind? ConjunctiveKind { get; }

    IChainDescriptor? OptionalInner => null;
    IReadOnlyList<IChainDescriptor> Alternatives => Array.Empty<IChainDescriptor>();
    IChainDescriptor? RepetitionInner => null;
    IChainDescriptor? RepetitionSeparator => null;
    (int Min, int Max) RepetitionBounds => (0, 0);
}
