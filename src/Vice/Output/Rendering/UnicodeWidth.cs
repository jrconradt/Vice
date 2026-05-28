using System.Globalization;

namespace Vice.Display.Rendering;

internal static class UnicodeWidth
{
    public static int GetWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var allAscii = true;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] >= 0x80)
            {
                allAscii = false;
                break;
            }
        }
        if (allAscii)
        {
            return text.Length;
        }

        var width = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            width += GetElementWidth(element);
        }
        return width;
    }

    public static string PadRight(string text, int totalWidth)
    {
        var currentWidth = GetWidth(text);
        var padding = totalWidth - currentWidth;
        return padding <= 0 ? text : text + new string(' ', padding);
    }

    public static string Truncate(string text, int maxWidth, string ellipsis = "...")
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var textWidth = GetWidth(text);
        if (textWidth <= maxWidth)
        {
            return text;
        }

        var ellipsisWidth = GetWidth(ellipsis);
        var targetWidth = maxWidth - ellipsisWidth;
        if (targetWidth <= 0)
        {
            return ellipsis.Length <= maxWidth ? ellipsis[..maxWidth] : "";
        }

        var parts = new List<string>();
        var currentWidth = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var elemWidth = GetElementWidth(element);
            if (currentWidth + elemWidth > targetWidth)
            {
                break;
            }

            parts.Add(element);
            currentWidth += elemWidth;
        }

        parts.Add(ellipsis);
        return string.Concat(parts);
    }

    private static int GetElementWidth(string element)
    {
        if (element.Length == 0)
        {
            return 0;
        }

        var firstChar = element[0];

        if (IsZeroWidth(firstChar))
        {
            return 0;
        }

        if (element.Length > 1)
        {

            if (element.Contains('\uFE0F'))
            {
                return 2;
            }

            if (char.IsHighSurrogate(firstChar))
            {
                return IsWideCodepoint(char.ConvertToUtf32(firstChar, element[1])) ? 2 : 1;
            }
        }

        if (!char.IsHighSurrogate(firstChar))
        {
            return IsWide(firstChar) ? 2 : 1;
        }

        if (element.Length >= 2 && char.IsLowSurrogate(element[1]))
        {
            var codepoint = char.ConvertToUtf32(firstChar, element[1]);
            return IsWideCodepoint(codepoint) ? 2 : 1;
        }

        return 1;
    }

    private static bool IsZeroWidth(char c)
    {

        return c is
            '\u200B' or
            '\u200C' or
            '\u200D' or
            '\uFEFF' or
            (>= '\u0300' and <= '\u036F') or
            (>= '\u0483' and <= '\u0489') or
            (>= '\u0591' and <= '\u05BD') or
            (>= '\u0610' and <= '\u061A') or
            (>= '\u064B' and <= '\u065F') or
            (>= '\u0E31' and <= '\u0E3A') or
            (>= '\uFE00' and <= '\uFE0F') or
            (>= '\u20D0' and <= '\u20FF');
    }

    private static bool IsWide(char c)
    {

        return c is
            (>= '\u1100' and <= '\u115F') or
            (>= '\u2E80' and <= '\u303E') or
            (>= '\u3041' and <= '\u33BF') or
            (>= '\u3400' and <= '\u4DBF') or
            (>= '\u4E00' and <= '\uA4CF') or
            (>= '\uAC00' and <= '\uD7A3') or
            (>= '\uF900' and <= '\uFAFF') or
            (>= '\uFE10' and <= '\uFE19') or
            (>= '\uFE30' and <= '\uFE6F') or
            (>= '\uFF01' and <= '\uFF60') or
            (>= '\uFFE0' and <= '\uFFE6');
    }

    private static bool IsWideCodepoint(int codepoint)
    {

        return codepoint is
            (>= 0x1F000 and <= 0x1FAFF) or
            (>= 0x20000 and <= 0x2FA1F) or
            (>= 0x30000 and <= 0x3134F);
    }
}
