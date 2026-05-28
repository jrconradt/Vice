using Vice.Display.Rendering;
using Vice.Options;

namespace Vice.Session;

internal static class CapabilityOverrides
{
    public static TerminalCapabilities Apply(
        TerminalCapabilities baseCaps,
        IReadOnlyDictionary<string, string?> globalOptions)
    {
        var caps = baseCaps;
        if (globalOptions.ContainsKey(new NoColorOption().Name))
        {
            caps = caps.WithoutColor();
        }

        if (globalOptions.TryGetValue(new ColorOption().Name, out var colorValue))
        {
            caps = (colorValue ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "never" or "off" or "no" => caps.WithoutColor(),
                "always" or "on" or "yes" => caps.WithForcedColor(),
                _ => caps,
            };
        }
        return caps;
    }
}
