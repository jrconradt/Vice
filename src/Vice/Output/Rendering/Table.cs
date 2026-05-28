namespace Vice.Display.Rendering;

public sealed class Table
{
    private readonly List<TableColumn> _columns = new();
    private readonly List<string[]> _rows = new();

    public TableBorder Border { get; set; } = TableBorder.Rounded;
    public Style? BorderStyle { get; set; }

    public Table AddColumn(string header, Action<TableColumn>? configure = null)
    {
        var column = new TableColumn(header);
        configure?.Invoke(column);
        _columns.Add(column);
        return this;
    }

    public Table AddRow(params string[] cells)
    {
        if (cells.Length != _columns.Count && _columns.Count > 0)
        {
            throw new ArgumentException($"Row has {cells.Length} cells but table has {_columns.Count} columns.");
        }

        while (_columns.Count < cells.Length)
        {
            _columns.Add(new TableColumn(""));
        }

        _rows.Add(cells);
        return this;
    }

    internal IReadOnlyList<string> Render(TerminalCapabilities caps)
    {
        return TableRenderer.Render(_columns, _rows, Border, BorderStyle, caps);
    }
}
