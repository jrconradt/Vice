using Vice.Commands;
using Vice.Help;
using Vice.Options;

namespace Vice.Manpages;

internal static class ManPageGenerator
{
    public static string Generate(
        string appName,
        string version,
        string nameLineDescription,
        string? bodyDescription,
        IReadOnlyList<CommandRegistration> visibleRegistrations,
        IReadOnlyList<GlobalOption> orderedGlobalOptions,
        DateTime? generatedOn = null)
    {
        var date = generatedOn is { } stamp
            ? stamp.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        var upperName = appName.ToUpperInvariant();

        var lines = new List<string>
        {
            $".TH {RoffEscape(upperName)} 1 \"{date}\" \"{RoffEscape(appName)} {RoffEscape(version)}\" \"User Commands\"",
            ".SH NAME",
            $"{RoffEscape(appName)} \\- {RoffEscape(nameLineDescription)}",
            ".SH SYNOPSIS",
            $".B {RoffEscape(appName)}",
            "[\\fIOPTIONS\\fR] \\fICOMMAND\\fR [\\fIARGS\\fR]",
            ".SH DESCRIPTION",
        };

        if (bodyDescription is not null)
        {
            lines.Add(RoffEscape(bodyDescription));
        }

        lines.Add($"Run \\fB{RoffEscape(appName)} help\\fR for the in-tool command summary, or \\fB{RoffEscape(appName)} help \\fI<command>\\fR for per-command help.");

        if (visibleRegistrations.Count > 0)
        {
            lines.Add(".SH COMMANDS");
            foreach (var reg in visibleRegistrations)
            {
                var usage = HelpFormatter.FormatChain(reg.Chain);
                lines.Add(".TP");
                lines.Add($".B {RoffEscape(usage)}");
                lines.Add(RoffEscape(reg.Description));
            }
        }

        if (orderedGlobalOptions.Count > 0)
        {
            lines.Add(".SH GLOBAL OPTIONS");
            foreach (var opt in orderedGlobalOptions)
            {
                lines.Add(".TP");
                var names = new List<string>(opt.Aliases.Count + 1);
                foreach (var a in opt.Aliases)
                {
                    names.Add(a.Length == 1 ? $"\\-{RoffEscape(a)}" : $"\\-\\-{RoffEscape(a)}");
                }

                names.Add($"\\-\\-{RoffEscape(opt.Name)}");
                var joined = string.Join(", ", names);
                lines.Add(opt.TakesValue
                    ? $".BR {joined} \" \" \\fIVALUE\\fR"
                    : $".B {joined}");
                lines.Add(RoffEscape(opt.Description));
            }
        }

        lines.AddRange(new[]
        {
            ".SH EXIT STATUS",
            ".TP",
            ".B 0",
            "Success.",
            ".TP",
            ".B 1",
            "Generic runtime error.",
            ".TP",
            ".B 2",
            "Usage error (unknown command, bad argument).",
            ".TP",
            ".B 130",
            "Interrupted by SIGINT (Ctrl+C).",
            ".SH ENVIRONMENT",
            ".TP",
            $".B {upperName}_LOG_LEVEL",
            "Logger threshold (trace|debug|info|warn|error).",
            ".TP",
            ".B NO_COLOR, FORCE_COLOR, CLICOLOR_FORCE",
            "Standard color-control env vars.",
            ".TP",
            ".B XDG_CONFIG_HOME, XDG_DATA_HOME, XDG_CACHE_HOME, XDG_STATE_HOME",
            $"Standard XDG directories. Each can be overridden per-app via {upperName}_CONFIG_HOME et al.",
            ".SH SEE ALSO",
            $".BR {appName} (1)",
        });

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string RoffEscape(string s)
        => string.Concat(s.Select((c, i) => c switch
        {
            '\\' => "\\\\",
            '-' => "\\-",
            '\'' when i == 0 => "\\(aq",
            '.' when i == 0 => "\\&.",
            _ => c.ToString(),
        }));
}
