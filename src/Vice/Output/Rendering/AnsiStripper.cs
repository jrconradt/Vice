using System.Text.RegularExpressions;

namespace Vice.Display.Rendering;

internal static partial class AnsiStripper
{

    [GeneratedRegex(@"\u001b\[[0-9;]*[A-Za-z]|\u001b\[\?[0-9;]*[hl]|\u001b\][^\u0007]*\u0007")]
    private static partial Regex AnsiPattern();

    public static string Strip(string text)
    {
        if (text.Length == 0 || !text.Contains('\u001b'))
        {
            return text;
        }

        return AnsiPattern().Replace(text, "");
    }
}
