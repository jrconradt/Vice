namespace Vice.Mux.Routing;

public sealed class Condition
{
    private readonly int[]? _codes;

    private Condition(int[]? codes)
    {
        _codes = codes;
    }

    public static readonly Condition Any = new(null);

    public bool IsWildcard => _codes is null;

    public static Condition Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            throw new ArgumentException("route: condition is empty");
        }

        var trimmed = spec.Trim();
        if (trimmed == "*"
            || string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
        {
            return Any;
        }

        var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException($"route: condition '{spec}' lists no codes");
        }

        var codes = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out codes[i]))
            {
                throw new ArgumentException($"route: condition '{spec}': '{parts[i]}' is not an integer exit code");
            }
        }

        return new Condition(codes);
    }

    public bool Matches(int code)
    {
        if (_codes is null)
        {
            return true;
        }

        for (int i = 0; i < _codes.Length; i++)
        {
            if (_codes[i] == code)
            {
                return true;
            }
        }

        return false;
    }
}
