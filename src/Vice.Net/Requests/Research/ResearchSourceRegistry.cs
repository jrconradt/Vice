using Vice.Logging;

namespace Vice.Net.Research;

internal sealed class ResearchSourceRegistry
{
    private readonly Dictionary<string, IResearchSource> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IResearchSource> _sources = new();

    public ResearchSourceRegistry()
        : this(new IResearchSource[]
               {
                   new ArxivSource(),
                   new GutenbergSource(),
                   new PubMedSource(),
                   new UniProtSource(),
                   new AlphaFoldSource(),
               })
    {
    }

    public ResearchSourceRegistry(IEnumerable<IResearchSource> sources)
    {
        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        foreach (var source in sources)
        {
            Register(source);
        }
    }

    public void Register(IResearchSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        _sources.Add(source);
        _byKey[source.Name] = source;
        foreach (var alias in source.Aliases)
        {
            _byKey[alias] = source;
        }
    }

    public IResearchSource Resolve(string name)
    {
        if (_byKey.TryGetValue(name, out var source))
        {
            return source;
        }

        var known = string.Join(", ", _sources.Select(s => s.Name));
        throw new BadArgument($"Unknown research source '{name}'. Known sources: {known}.");
    }
}
