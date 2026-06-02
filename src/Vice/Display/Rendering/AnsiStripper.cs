using System.Text.RegularExpressions;

namespace Vice.Display.Rendering;

internal static partial class AnsiStripper
{
    [GeneratedRegex(@"\u001b\[[\u0030-\u003f]*[\u0020-\u002f]*[\u0040-\u007e]|\u001b\][^\u0007\u001b]*(?:\u0007|\u001b\\)|\u001b[PX\u005e\u005f][^\u001b]*\u001b\\|\u001b[\u0020-\u002f]*[\u0030-\u007e]|\u001b|[\u0000-\u0008\u000b-\u001f\u007f]")]
    private static partial Regex ControlPattern();

    public static string Strip(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        return ControlPattern().Replace(text, "");
    }
}
