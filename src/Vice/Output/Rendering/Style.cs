namespace Vice.Display.Rendering;

public readonly record struct Style(
    Color? Foreground = null,
    Color? Background = null,
    bool Bold = false,
    bool Dim = false,
    bool Italic = false,
    bool Underline = false,
    bool Strikethrough = false)
{
    public static readonly Style Default = new();

    public Style Fg(Color color) => this with { Foreground = color };
    public Style Bg(Color color) => this with { Background = color };
    public Style WithBold() => this with { Bold = true };
    public Style WithDim() => this with { Dim = true };
    public Style WithItalic() => this with { Italic = true };
    public Style WithUnderline() => this with { Underline = true };
    public Style WithStrikethrough() => this with { Strikethrough = true };

    public bool IsDefault =>
        Foreground is null &&
        Background is null &&
        !Bold && !Dim && !Italic && !Underline && !Strikethrough;
}
