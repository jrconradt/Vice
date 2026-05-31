namespace Vice.Parser;

public sealed class OptionRegistry
{
    private readonly Dictionary<string, OptionMetadata> _byName =
        new(StringComparer.Ordinal);

    private readonly List<OptionMetadata> _all = new();

    private bool _frozen;

    public IReadOnlyList<OptionMetadata> All => _all;

    public void Add(OptionMetadata metadata)
    {
        if (_frozen)
        {
            throw new InvalidOperationException("OptionRegistry is frozen.");
        }

        _all.Add(metadata);
        _byName[metadata.CanonicalName] = metadata;
        foreach (var alias in metadata.Aliases)
        {
            _byName[alias] = metadata;
        }
    }

    public void Freeze()
    {
        _frozen = true;
    }

    public bool TryResolve(string anyName, out OptionMetadata metadata)
    {
        if (_byName.TryGetValue(anyName, out var found))
        {
            metadata = found;
            return true;
        }
        metadata = default!;
        return false;
    }
}

public sealed record OptionMetadata(
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    bool TakesValue,
    bool Required,
    IReadOnlyList<string>? AcceptedValues = null,
    Func<int, bool>? RangeValidator = null);
