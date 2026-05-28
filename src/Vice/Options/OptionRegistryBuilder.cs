using Vice.Parser;

namespace Vice.Options;

public static class OptionRegistryBuilder
{
    public static OptionRegistry Build(IEnumerable<GlobalOption> options)
    {
        var registry = new OptionRegistry();
        foreach (var opt in options)
        {
            registry.Add(ToMetadata(opt));
        }

        registry.Freeze();
        return registry;
    }

    private static OptionMetadata ToMetadata(GlobalOption opt)
    {
        IReadOnlyList<string>? accepted = opt is ValueBearingOption vb && vb.AcceptedValues is not null
            ? vb.AcceptedValues.ToArray()
            : null;
        Func<int, bool>? range = opt is RangeBearingOption rb ? rb.Validate : null;
        return new OptionMetadata(opt.Name,
                                  opt.Aliases,
                                  opt.TakesValue,
                                  opt.Required,
                                  accepted,
                                  range);
    }
}
