using Vice.Contracts;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Options;
using Vice.Parser;

namespace Vice.Help;

internal sealed class HelpBuilder
{
    private static readonly Style SectionHeader = new Style().Fg(Color.Yellow).WithBold();
    private static readonly Style AppTitle = new Style().WithBold();
    private static readonly Style Verb = new Style().Fg(Color.Cyan);
    private static readonly Style RequiredTarget = new Style().Fg(Color.Green);
    private static readonly Style OptionalTarget = new Style().Fg(Color.Green).WithDim();
    private static readonly Style OperatorStyle = new Style().Fg(Color.Magenta);
    private static readonly Style DimText = new Style().WithDim();

    public static void WriteHelp(
        string? title,
        IReadOnlyList<CommandRegistration> visibleRegistrations,
        IReadOnlyList<GlobalOption> orderedGlobalOptions,
        RenderContext render)
    {
        if (!string.IsNullOrEmpty(title))
        {
            render.WriteLine(title, AppTitle);
            render.WriteLine();
        }

        if (visibleRegistrations.Count > 0)
        {
            render.WriteLine("COMMANDS:", SectionHeader);

            var lines = new List<(string usage, string desc)>();
            int maxUsage = 0;

            foreach (var reg in visibleRegistrations)
            {
                var usage = HelpFormatter.FormatChain(reg.Chain);
                lines.Add((usage, reg.Description));
                var usageWidth = UnicodeWidth.GetWidth(usage);
                if (usageWidth > maxUsage)
                {
                    maxUsage = usageWidth;
                }
            }

            foreach (var (usage, desc) in lines)
            {
                var padded = UnicodeWidth.PadRight(usage, maxUsage + 2);
                render.Console.WriteLine($"  {render.Styled(padded, Verb)} {desc}");
            }

            render.WriteLine();
        }

        if (orderedGlobalOptions.Count > 0)
        {
            render.WriteLine("GLOBAL OPTIONS:", SectionHeader);

            var entries = orderedGlobalOptions
                .Select(o => (
                    usage: FormatOptionUsage(o),
                    desc: o.Description))
                .ToList();

            int maxUsageOpt = entries.Max(e => UnicodeWidth.GetWidth(e.usage));
            foreach (var (usage, desc) in entries)
            {
                var padded = UnicodeWidth.PadRight(usage, maxUsageOpt + 2);
                render.Console.WriteLine($"  {render.Styled(padded, Verb)} {desc}");
            }
        }
    }

    private static string FormatOptionUsage(GlobalOption o)
    {
        var aliasPart = string.Concat(o.Aliases.Select(a => a.Length == 1 ? $"-{a}, " : $"--{a}, "));
        return o.TakesValue
            ? $"{aliasPart}--{o.Name} <{o.Name}>"
            : $"{aliasPart}--{o.Name}";
    }

    public static void WriteCommandHelp(
        CommandRegistration registration,
        IConsoleWriter console)
    {
        WriteCommandHelp(registration, new RenderContext(console, TerminalCapabilities.None));
    }

    public static void WriteCommandHelp(
        CommandRegistration registration,
        RenderContext render)
    {
        var usage = HelpFormatter.FormatChain(registration.Chain);
        render.WriteLine(usage, Verb);
        render.WriteLine($"  {registration.Description}");

        var targetList = HelpFormatter.FormatTargetList(registration.Chain);
        if (targetList.Count > 0)
        {
            render.WriteLine();
            render.WriteLine("TARGETS:", SectionHeader);

            int maxName = targetList.Max(t => UnicodeWidth.GetWidth(t.Name));
            foreach (var (name, info, isOptional) in targetList)
            {
                var padded = UnicodeWidth.PadRight(name, maxName + 2);
                render.Console.WriteLine($"  {render.Styled(padded, isOptional ? OptionalTarget : RequiredTarget)} {info}");
            }
        }

        var chain = (IChainDescriptor)registration.Chain;
        if (chain.Synonyms.Count > 0)
        {
            render.WriteLine();
            render.Console.WriteLine($"{render.Styled("SYNONYMS:", SectionHeader)} {render.Styled(string.Join(", ", chain.Synonyms), DimText)}");
        }

        if (HelpFormatter.HasPipelineStages(chain))
        {
            var stages = HelpFormatter.FormatPipelineStages(chain);

            render.WriteLine();
            render.WriteLine("PIPELINE:", SectionHeader);

            foreach (var stage in stages)
            {
                if (stage.OperatorWord is null)
                {
                    render.Console.WriteLine($"  Stage {stage.StageNumber}: {render.Styled(stage.FormattedChain, Verb)}");
                }
                else
                {
                    render.Console.WriteLine($"  Stage {stage.StageNumber} ({render.Styled(stage.OperatorWord, OperatorStyle)}): {render.Styled(stage.FormattedChain, Verb)}");
                }

                if (stage.Targets.Count > 0)
                {
                    int maxTarget = stage.Targets.Max(t => UnicodeWidth.GetWidth(t.Name));
                    foreach (var entry in stage.Targets)
                    {
                        var padded = UnicodeWidth.PadRight(entry.Name, maxTarget + 2);
                        render.Console.WriteLine($"    {render.Styled(padded, RequiredTarget)} {entry.Info}");
                    }
                }
            }

            var usedOperators = stages
                .Where(s => s.OperatorWord is not null)
                .Select(s => s.OperatorWord!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (usedOperators.Count > 0)
            {
                render.WriteLine();
                render.WriteLine("OPERATORS:", SectionHeader);

                int maxOp = usedOperators.Max(o => UnicodeWidth.GetWidth(o));
                foreach (var op in usedOperators)
                {
                    var desc = HelpFormatter.OperatorDescriptions.TryGetValue(op, out var d) ? d : "";
                    var padded = UnicodeWidth.PadRight(op, maxOp + 2);
                    render.Console.WriteLine($"  {render.Styled(padded, OperatorStyle)} {desc}");
                }
            }
        }
    }
}
