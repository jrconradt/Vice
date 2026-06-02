namespace Vice.Display.Rendering;

internal static class BorderChars
{
    public static BorderSet GetBorderSet(TableBorder border, bool supportsUnicode)
    {
        if (!supportsUnicode && border != TableBorder.Ascii
            && border != TableBorder.None)
        {
            border = TableBorder.Ascii;
        }

        return border switch
        {
            TableBorder.None => BorderSet.Empty,
            TableBorder.Ascii => new BorderSet("+", "-", "+", "+", "|", "+", "-", "+", "+", "|", "+", "-", "+", "+", "+", "-", "+"),
            TableBorder.Rounded => new BorderSet("╭", "─", "┬", "╮", "│", "├", "─", "┼", "┤", "│", "╰", "─", "┴", "╯", "├", "─", "┤"),
            TableBorder.Heavy => new BorderSet("┏", "━", "┳", "┓", "┃", "┣", "━", "╋", "┫", "┃", "┗", "━", "┻", "┛", "┣", "━", "┫"),
            TableBorder.Double => new BorderSet("╔", "═", "╦", "╗", "║", "╠", "═", "╬", "╣", "║", "╚", "═", "╩", "╝", "╠", "═", "╣"),
            _ => BorderSet.Empty
        };
    }
}
