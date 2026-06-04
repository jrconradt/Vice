namespace Vice.Parser;

internal sealed class HeadIndex
{
    private readonly HashSet<string> _exact;
    private readonly List<string> _names;

    private HeadIndex(HashSet<string> exact, List<string> names)
    {
        _exact = exact;
        _names = names;
    }

    public bool ContainsExact(string token) => _exact.Contains(token);

    public IReadOnlyList<string> Names => _names;

    public static HeadIndex Build(IReadOnlyList<IChainDescriptor> registrations)
    {
        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var reg in registrations)
        {
            if (reg.Kind != ChainNodeKind.Word)
            {
                continue;
            }

            if (exact.Add(reg.Name))
            {
                names.Add(reg.Name);
            }

            foreach (var s in reg.Synonyms)
            {
                if (exact.Add(s))
                {
                    names.Add(s);
                }
            }
        }

        return new HeadIndex(exact, names);
    }

    public bool HasExactHeadMatch(string token) => ContainsExact(token);

    public (bool ok, string? chosenName, List<string>? candidates) TryPrefixHead(string token)
    {
        List<string>? matches = null;

        foreach (var name in _names)
        {
            if (name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                matches ??= new List<string>();
                matches.Add(name);
            }
        }

        if (matches is null || matches.Count == 0)
        {
            return (false, null, null);
        }

        if (matches.Count == 1)
        {
            return (true, matches[0], null);
        }

        return (false, null, matches);
    }
}
