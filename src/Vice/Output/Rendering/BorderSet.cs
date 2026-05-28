namespace Vice.Display.Rendering;

internal readonly record struct BorderSet(
    string TopLeft, string TopHorizontal, string TopJunction, string TopRight,
    string Left,
    string MiddleLeft, string MiddleHorizontal, string MiddleJunction, string MiddleRight,
    string Right,
    string BottomLeft, string BottomHorizontal, string BottomJunction, string BottomRight,
    string HeaderLeft, string HeaderHorizontal, string HeaderRight)
{
    public static readonly BorderSet Empty = new("", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
    public bool IsEmpty => TopLeft.Length == 0;
}
