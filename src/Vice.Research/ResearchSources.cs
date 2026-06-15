using Vice.Logging;

namespace Vice.Research;

public static class ResearchSources
{
    private static readonly IResearchSource[] All =
    {
        new ArxivSource(),
        new GutenbergSource(),
        new PubMedSource(),
        new UniProtSource(),
        new AlphaFoldSource(),
    };

    public static IResearchSource Resolve(string name)
    {
        foreach (var source in All)
        {
            if (string.Equals(source.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }

            foreach (var alias in source.Aliases)
            {
                if (string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
                {
                    return source;
                }
            }
        }

        var known = string.Join(", ", All.Select(s => s.Name));
        throw new BadArgument($"Unknown research source '{name}'. Known sources: {known}.");
    }
}
