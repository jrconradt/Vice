namespace Vice.Display.Rendering;

internal static class TableRenderer
{
    public static IReadOnlyList<string> Render(
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<string[]> rows,
        TableBorder border,
        Style? borderStyle,
        TerminalCapabilities caps)
    {
        if (columns.Count == 0)
        {
            return Array.Empty<string>();
        }

        var colWidths = CalculateColumnWidths(columns, rows, caps.Width, border != TableBorder.None);
        var borders = BorderChars.GetBorderSet(border, caps.SupportsUnicode);
        var lines = new List<string>();
        var colorDepth = caps.SupportsColor ? caps.ColorDepth : ColorDepth.None;

        if (!borders.IsEmpty)
        {
            lines.Add(BuildHorizontalLine(borders.TopLeft, borders.TopHorizontal, borders.TopJunction, borders.TopRight, colWidths, borderStyle, colorDepth));
        }

        lines.Add(BuildDataRow(columns, null, colWidths, borders, isHeader: true, borderStyle, colorDepth));

        if (!borders.IsEmpty)
        {
            lines.Add(BuildHorizontalLine(borders.HeaderLeft, borders.HeaderHorizontal, borders.MiddleJunction, borders.HeaderRight, colWidths, borderStyle, colorDepth));
        }

        for (int r = 0; r < rows.Count; r++)
        {
            lines.Add(BuildDataRow(columns, rows[r], colWidths, borders, isHeader: false, borderStyle, colorDepth));
        }

        return lines;
    }

    private static int[] CalculateColumnWidths(
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<string[]> rows,
        int terminalWidth,
        bool hasBorders)
    {
        var widths = new int[columns.Count];

        for (int c = 0; c < columns.Count; c++)
        {
            widths[c] = UnicodeWidth.GetWidth(columns[c].Header);
        }

        foreach (var row in rows)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                var cellWidth = UnicodeWidth.GetWidth(row[c]);
                widths[c] = Math.Max(widths[c], cellWidth);
            }
        }

        for (int c = 0; c < columns.Count; c++)
        {
            if (columns[c].MinWidth is { } min)
            {
                widths[c] = Math.Max(widths[c], min);
            }

            if (columns[c].MaxWidth is { } max)
            {
                widths[c] = Math.Min(widths[c], max);
            }
        }

        var borderOverhead = hasBorders ? 3 * columns.Count + 1 : columns.Count > 1 ? 2 * (columns.Count - 1) : 0;
        var availableWidth = terminalWidth - borderOverhead;

        var totalWidth = widths.Sum();
        if (totalWidth > availableWidth && availableWidth > 0)
        {

            var ratio = (double)availableWidth / totalWidth;
            for (int c = 0; c < widths.Length; c++)
            {
                widths[c] = Math.Max(1, (int)(widths[c] * ratio));
            }
        }

        return widths;
    }

    private static string BuildHorizontalLine(
        string left, string horizontal, string junction, string right,
        int[] colWidths, Style? borderStyle, ColorDepth colorDepth)
    {
        var parts = new List<string> { left };
        for (int c = 0; c < colWidths.Length; c++)
        {
            var count = colWidths[c] + 2;
            if (horizontal.Length == 1)
            {
                parts.Add(new string(horizontal[0], count));
            }
            else
            {
                parts.Add(string.Concat(Enumerable.Repeat(horizontal, count)));
            }
            parts.Add(c < colWidths.Length - 1 ? junction : right);
        }

        var line = string.Concat(parts);
        return borderStyle is { } s ? AnsiBuilder.Apply(line, s, colorDepth) : line;
    }

    private static string BuildDataRow(
        IReadOnlyList<TableColumn> columns,
        string[]? cells,
        int[] colWidths,
        BorderSet borders,
        bool isHeader,
        Style? borderStyle,
        ColorDepth colorDepth)
    {
        var parts = new List<string>();
        var hasBorders = !borders.IsEmpty;
        var separator = hasBorders ? borders.Left : "";

        if (hasBorders)
        {
            parts.Add(ApplyBorderStyle(separator, borderStyle, colorDepth));
        }

        for (int c = 0; c < columns.Count; c++)
        {
            var text = isHeader ? columns[c].Header : cells![c];
            var width = colWidths[c];
            var alignment = columns[c].Alignment;
            var style = isHeader ? columns[c].HeaderStyle : columns[c].CellStyle;

            var textWidth = UnicodeWidth.GetWidth(text);
            if (textWidth > width)
            {
                text = UnicodeWidth.Truncate(text, width);
                textWidth = UnicodeWidth.GetWidth(text);
            }

            var aligned = Align(text,
                                textWidth,
                                width,
                                alignment);

            if (hasBorders)
            {
                parts.Add(" ");
            }

            if (style is { } s && colorDepth != ColorDepth.None)
            {
                parts.Add(AnsiBuilder.Apply(aligned, s, colorDepth));
            }
            else
            {
                parts.Add(aligned);
            }

            if (hasBorders)
            {
                parts.Add(" ");
            }

            if (c < columns.Count - 1)
            {
                if (hasBorders)
                {
                    parts.Add(ApplyBorderStyle(borders.Right, borderStyle, colorDepth));
                }
                else
                {
                    parts.Add("  ");
                }
            }
        }

        if (hasBorders)
        {
            parts.Add(ApplyBorderStyle(borders.Right, borderStyle, colorDepth));
        }

        return string.Concat(parts);
    }

    private static string Align(string text,
                                int textWidth,
                                int width,
                                Alignment alignment)
    {
        var padding = width - textWidth;
        if (padding <= 0)
        {
            return text;
        }

        return alignment switch
        {
            Alignment.Right => new string(' ', padding) + text,
            Alignment.Center =>
                new string(' ', padding / 2) + text + new string(' ', padding - padding / 2),
            _ => text + new string(' ', padding)
        };
    }

    private static string ApplyBorderStyle(string text, Style? style, ColorDepth colorDepth)
    {
        return style is { } s && colorDepth != ColorDepth.None
            ? AnsiBuilder.Apply(text, s, colorDepth)
            : text;
    }
}
