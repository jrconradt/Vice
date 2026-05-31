namespace Vice.Display.Rendering;

public sealed class RenderContext
{
    private readonly IConsoleWriter _console;
    private readonly TerminalCapabilities _caps;

    public RenderContext(IConsoleWriter console, TerminalCapabilities capabilities)
    {
        _console = console;
        _caps = capabilities;
    }

    public TerminalCapabilities Capabilities => _caps;
    public IConsoleWriter Console => _console;

    public void Write(string text) => _console.Write(text);

    public void Write(string text, Style style)
    {
        _console.Write(_caps.SupportsColor ? AnsiBuilder.Apply(text, style, _caps.ColorDepth) : text);
    }

    public void WriteLine(string text) => _console.WriteLine(text);

    public void WriteLine(string text, Style style)
    {
        _console.WriteLine(_caps.SupportsColor ? AnsiBuilder.Apply(text, style, _caps.ColorDepth) : text);
    }

    public void WriteLine() => _console.WriteLine();

    public void WriteError(string text) => _console.WriteError(text);

    public void WriteError(string text, Style style)
    {
        _console.WriteError(_caps.SupportsColor ? AnsiBuilder.Apply(text, style, _caps.ColorDepth) : text);
    }

    public void WriteTable(Table table)
    {
        var lines = table.Render(_caps);
        foreach (var line in lines)
        {
            _console.WriteLine(line);
        }
    }

    public string Styled(string text, Style style)
    {
        return _caps.SupportsColor ? AnsiBuilder.Apply(text, style, _caps.ColorDepth) : text;
    }

    public void WriteRule(string? label = null, Style? style = null)
    {
        var width = _caps.Width;
        if (label is null)
        {
            var ruleChar = _caps.SupportsUnicode ? '\u2500' : '-';
            var line = new string(ruleChar, width);
            if (style is { } s)
            {
                WriteLine(line, s);
            }
            else
            {
                WriteLine(line);
            }

            return;
        }

        var padded = $" {label} ";
        var paddedWidth = UnicodeWidth.GetWidth(padded);
        var remaining = width - paddedWidth;
        var leftLen = remaining / 2;
        var rightLen = remaining - leftLen;
        var rChar = _caps.SupportsUnicode ? '\u2500' : '-';
        var left = new string(rChar, Math.Max(0, leftLen));
        var right = new string(rChar, Math.Max(0, rightLen));
        var fullLine = $"{left}{padded}{right}";

        if (style is { } st)
        {
            WriteLine(fullLine, st);
        }
        else
        {
            WriteLine(fullLine);
        }
    }
}
