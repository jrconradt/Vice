namespace Vice.Display.Rendering;

internal static class AnsiBuilder
{
    private const string RESET = "\u001b[0m";

    public static string Apply(string text, Style style, ColorDepth depth)
    {
        if (style.IsDefault || depth == ColorDepth.None)
        {
            return text;
        }

        var prefix = BuildPrefix(style, depth);
        if (prefix.Length == 0)
        {
            return text;
        }

        return $"{prefix}{text}{RESET}";
    }

    private static string BuildPrefix(Style style, ColorDepth depth)
    {
        var parts = new List<string>(7);

        if (style.Bold)
        {
            parts.Add("\u001b[1m");
        }

        if (style.Dim)
        {
            parts.Add("\u001b[2m");
        }

        if (style.Italic)
        {
            parts.Add("\u001b[3m");
        }

        if (style.Underline)
        {
            parts.Add("\u001b[4m");
        }

        if (style.Strikethrough)
        {
            parts.Add("\u001b[9m");
        }

        if (style.Foreground is { } fg)
        {
            var fgCode = fg.ToAnsiFg(depth);
            if (fgCode.Length > 0)
            {
                parts.Add(fgCode);
            }
        }

        if (style.Background is { } bg)
        {
            var bgCode = bg.ToAnsiBg(depth);
            if (bgCode.Length > 0)
            {
                parts.Add(bgCode);
            }
        }

        return string.Concat(parts);
    }
}
