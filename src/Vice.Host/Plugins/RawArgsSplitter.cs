namespace Vice.Plugins;

internal static class RawArgsSplitter
{
    private static readonly HashSet<string> PipingWords =
        new(StringComparer.OrdinalIgnoreCase) { "then", "pipe", "send", "and", "or", "fan" };

    public static bool ContainsPiping(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (PipingWords.Contains(args[i]))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ContainsFan(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "fan", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static List<Segment> Split(string[] args)
    {
        var segments = new List<Segment>();
        var current = new List<string>();
        string? pendingOp = null;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (PipingWords.Contains(a))
            {
                segments.Add(new Segment(current.ToArray(), pendingOp));
                current = new List<string>();
                pendingOp = a;
            }
            else
            {
                current.Add(a);
            }
        }
        segments.Add(new Segment(current.ToArray(), pendingOp));
        return segments;
    }

    public sealed record Segment(string[] Args, string? OperatorWord);
}
