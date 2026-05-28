namespace Vice.Display.Rendering;

public sealed class TerminalCapabilities
{
    public bool SupportsAnsi { get; }
    public bool SupportsColor { get; }
    public ColorDepth ColorDepth { get; }
    public int Width { get; }
    public bool IsInteractive { get; }
    public bool IsInputInteractive { get; }
    public bool SupportsUnicode { get; }

    public TerminalCapabilities(
        bool supportsAnsi,
        bool supportsColor,
        ColorDepth colorDepth,
        int width,
        bool isInteractive,
        bool supportsUnicode,
        bool isInputInteractive = false)
    {
        SupportsAnsi = supportsAnsi;
        SupportsColor = supportsColor;
        ColorDepth = colorDepth;
        Width = width;
        IsInteractive = isInteractive;
        IsInputInteractive = isInputInteractive;
        SupportsUnicode = supportsUnicode;
    }

    public static TerminalCapabilities None { get; } = new(
        supportsAnsi: false,
        supportsColor: false,
        colorDepth: ColorDepth.None,
        width: 80,
        isInteractive: false,
        supportsUnicode: false,
        isInputInteractive: false);

    public TerminalCapabilities WithoutColor() => new(
        SupportsAnsi,
        supportsColor: false,
        colorDepth: ColorDepth.None,
        Width,
        IsInteractive,
        SupportsUnicode,
        IsInputInteractive);

    public TerminalCapabilities WithForcedColor() => new(
        supportsAnsi: true,
        supportsColor: true,
        colorDepth: ColorDepth == ColorDepth.None ? ColorDepth.TrueColor : ColorDepth,
        Width,
        IsInteractive,
        SupportsUnicode,
        IsInputInteractive);

    public static TerminalCapabilities Detect()
    {
        var noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;
        var forceColorEnv = Environment.GetEnvironmentVariable("FORCE_COLOR");
        var forceColor = !string.IsNullOrEmpty(forceColorEnv);
        var cliColorForce = Environment.GetEnvironmentVariable("CLICOLOR_FORCE");
        var cliColorForceOn = !string.IsNullOrEmpty(cliColorForce) && cliColorForce != "0";
        var forcedOn = (forceColor || cliColorForceOn) && !noColor;

        var term = Environment.GetEnvironmentVariable("TERM") ?? "";
        var colorTerm = Environment.GetEnvironmentVariable("COLORTERM") ?? "";

        bool isOutputRedirected;
        try
        {
            isOutputRedirected = Console.IsOutputRedirected;
        }
        catch
        {
            isOutputRedirected = true;
        }

        bool isErrorRedirected;
        try
        {
            isErrorRedirected = Console.IsErrorRedirected;
        }
        catch
        {
            isErrorRedirected = true;
        }

        bool isInputRedirected;
        try
        {
            isInputRedirected = Console.IsInputRedirected;
        }
        catch
        {
            isInputRedirected = true;
        }

        var isInteractive = !isOutputRedirected;
        var isInputInteractive = !isInputRedirected;
        var isDumb = term.Equals("dumb", StringComparison.OrdinalIgnoreCase);
        var supportsAnsi = forcedOn || (isInteractive && !isDumb);
        var supportsColor = forcedOn || (supportsAnsi && !noColor);

        var colorDepth = ColorDepth.None;
        if (supportsColor)
        {
            if (colorTerm.Equals("truecolor", StringComparison.OrdinalIgnoreCase) ||
                colorTerm.Equals("24bit", StringComparison.OrdinalIgnoreCase))
            {
                colorDepth = ColorDepth.TrueColor;
            }
            else if (term.Contains("256color", StringComparison.OrdinalIgnoreCase))
            {
                colorDepth = ColorDepth.Color256;
            }
            else
            {
                colorDepth = forcedOn ? ColorDepth.TrueColor : ColorDepth.Basic8;
            }
        }

        int width;
        try
        {
            width = Console.WindowWidth;
        }
        catch
        {
            width = 80;
        }
        if (width <= 0)
        {
            width = 80;
        }

        var supportsUnicode = DetectUnicode();

        return new TerminalCapabilities(supportsAnsi, supportsColor, colorDepth, width, isInteractive, supportsUnicode, isInputInteractive);
    }

    private static bool DetectUnicode()
    {
        var lang = Environment.GetEnvironmentVariable("LANG") ?? "";
        var lcAll = Environment.GetEnvironmentVariable("LC_ALL") ?? "";
        var lcCtype = Environment.GetEnvironmentVariable("LC_CTYPE") ?? "";

        if (lang.Contains("UTF-8", StringComparison.OrdinalIgnoreCase) ||
            lang.Contains("UTF8", StringComparison.OrdinalIgnoreCase) ||
            lcAll.Contains("UTF-8", StringComparison.OrdinalIgnoreCase) ||
            lcAll.Contains("UTF8", StringComparison.OrdinalIgnoreCase) ||
            lcCtype.Contains("UTF-8", StringComparison.OrdinalIgnoreCase) ||
            lcCtype.Contains("UTF8", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Environment.GetEnvironmentVariable("WT_SESSION") is not null)
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        return Console.OutputEncoding.WebName.Contains("utf", StringComparison.OrdinalIgnoreCase);
    }
}
