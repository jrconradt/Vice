using System.Collections.Frozen;

namespace Vice.Display.Rendering;

public readonly record struct Color
{
    private static readonly FrozenDictionary<int, string> Fg256Cache = BuildCache("[38;5;");
    private static readonly FrozenDictionary<int, string> Bg256Cache = BuildCache("[48;5;");

    private static FrozenDictionary<int, string> BuildCache(string prefix)
    {
        var dict = new Dictionary<int, string>(256);
        for (int i = 0; i < 256; i++)
        {
            dict[i] = $"{prefix}{i}m";
        }

        return dict.ToFrozenDictionary();
    }

    private readonly ColorKind _kind;
    private readonly byte _index;
    private readonly byte _r, _g, _b;

    private enum ColorKind : byte { Basic, Color256, Rgb }

    private Color(byte index) : this()
    {
        _kind = ColorKind.Basic;
        _index = index;
    }

    private Color(byte index, ColorKind kind) : this()
    {
        _kind = kind;
        _index = index;
    }

    private Color(byte r, byte g, byte b) : this()
    {
        _kind = ColorKind.Rgb;
        _r = r;
        _g = g;
        _b = b;
    }

    public static readonly Color Black = new(0);
    public static readonly Color Red = new(1);
    public static readonly Color Green = new(2);
    public static readonly Color Yellow = new(3);
    public static readonly Color Blue = new(4);
    public static readonly Color Magenta = new(5);
    public static readonly Color Cyan = new(6);
    public static readonly Color White = new(7);

    public static readonly Color BrightBlack = new(8, ColorKind.Basic);
    public static readonly Color BrightRed = new(9, ColorKind.Basic);
    public static readonly Color BrightGreen = new(10, ColorKind.Basic);
    public static readonly Color BrightYellow = new(11, ColorKind.Basic);
    public static readonly Color BrightBlue = new(12, ColorKind.Basic);
    public static readonly Color BrightMagenta = new(13, ColorKind.Basic);
    public static readonly Color BrightCyan = new(14, ColorKind.Basic);
    public static readonly Color BrightWhite = new(15, ColorKind.Basic);

    public static Color From256(byte index) => new(index, ColorKind.Color256);
    public static Color Rgb(byte r, byte g, byte b) => new(r, g, b);

    internal string ToAnsiFg(ColorDepth depth)
    {
        return depth switch
        {
            ColorDepth.None => "",
            _ => _kind switch
            {
                ColorKind.Basic => BasicFgCode(_index),
                ColorKind.Color256 => depth >= ColorDepth.Color256
                    ? Fg256Cache[_index]
                    : DowngradeToBasicFg(),
                ColorKind.Rgb => depth >= ColorDepth.TrueColor
                    ? $"\u001b[38;2;{_r};{_g};{_b}m"
                    : depth >= ColorDepth.Color256
                        ? Fg256Cache[RgbTo256(_r, _g, _b)]
                        : DowngradeRgbToBasicFg(),
                _ => ""
            }
        };
    }

    internal string ToAnsiBg(ColorDepth depth)
    {
        return depth switch
        {
            ColorDepth.None => "",
            _ => _kind switch
            {
                ColorKind.Basic => BasicBgCode(_index),
                ColorKind.Color256 => depth >= ColorDepth.Color256
                    ? Bg256Cache[_index]
                    : DowngradeToBasicBg(),
                ColorKind.Rgb => depth >= ColorDepth.TrueColor
                    ? $"\u001b[48;2;{_r};{_g};{_b}m"
                    : depth >= ColorDepth.Color256
                        ? Bg256Cache[RgbTo256(_r, _g, _b)]
                        : DowngradeRgbToBasicBg(),
                _ => ""
            }
        };
    }

    private string DowngradeToBasicFg()
    {
        return BasicFgCode(Index256ToBasic(_index));
    }

    private string DowngradeToBasicBg()
    {
        return BasicBgCode(Index256ToBasic(_index));
    }

    private string DowngradeRgbToBasicFg()
    {
        return BasicFgCode(RgbToBasic(_r, _g, _b));
    }

    private string DowngradeRgbToBasicBg()
    {
        return BasicBgCode(RgbToBasic(_r, _g, _b));
    }

    private static string BasicFgCode(byte basic)
    {
        return basic < 8 ? $"\u001b[{30 + basic}m" : $"\u001b[{90 + basic - 8}m";
    }

    private static string BasicBgCode(byte basic)
    {
        return basic < 8 ? $"\u001b[{40 + basic}m" : $"\u001b[{100 + basic - 8}m";
    }

    private static byte RgbTo256(byte r, byte g, byte b)
    {

        if (r == g && g == b)
        {
            if (r < 8)
            {
                return 16;
            }

            if (r > 248)
            {
                return 231;
            }

            return (byte)(Math.Round((r - 8.0) / 247.0 * 24.0) + 232);
        }

        var ri = (int)Math.Round(r / 255.0 * 5.0);
        var gi = (int)Math.Round(g / 255.0 * 5.0);
        var bi = (int)Math.Round(b / 255.0 * 5.0);
        return (byte)(16 + 36 * ri + 6 * gi + bi);
    }

    private static byte Index256ToBasic(byte index)
    {
        if (index < 16)
        {
            return index;
        }

        if (index >= 232)
        {

            return (byte)(index >= 244 ? 15 : 0);
        }

        var adjusted = index - 16;
        var r = adjusted / 36;
        var g = (adjusted % 36) / 6;
        var b = adjusted % 6;
        return RgbToBasic((byte)(r * 51), (byte)(g * 51), (byte)(b * 51));
    }

    private static byte RgbToBasic(byte r, byte g, byte b)
    {
        byte best = 0;
        var bestDist = int.MaxValue;

        ReadOnlySpan<(byte r, byte g, byte b)> basics =
        [
            (0, 0, 0),
            (170, 0, 0),
            (0, 170, 0),
            (170, 170, 0),
            (0, 0, 170),
            (170, 0, 170),
            (0, 170, 170),
            (170, 170, 170),
        ];

        for (int i = 0; i < basics.Length; i++)
        {
            var (br, bg, bb) = basics[i];

            var dist = DistSq(r, g, b, br, bg, bb);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = (byte)i;
            }

            var distBright = DistSq(r, g, b, (byte)Math.Min(br + 85, 255), (byte)Math.Min(bg + 85, 255), (byte)Math.Min(bb + 85, 255));
            if (distBright < bestDist)
            {
                bestDist = distBright;
                best = (byte)(i + 8);
            }
        }

        return best;
    }

    private static int DistSq(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var dr = r1 - r2;
        var dg = g1 - g2;
        var db = b1 - b2;
        return dr * dr + dg * dg + db * db;
    }
}
