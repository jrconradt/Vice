namespace Vice.Display.Rendering;

public sealed class TableColumn
{
    public string Header { get; }
    public Alignment Alignment { get; set; } = Alignment.Left;
    public int? MinWidth { get; set; }
    public int? MaxWidth { get; set; }
    public Style? HeaderStyle { get; set; }
    public Style? CellStyle { get; set; }

    public TableColumn(string header)
    {
        Header = header;
    }
}
