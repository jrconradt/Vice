namespace Vice.Logging;

public static class InvocationScope
{
    private static readonly AsyncLocal<string?> Ambient = new();

    public static string? Current => Ambient.Value;

    public static string Mint() => Guid.NewGuid().ToString("N").Substring(0, 12);

    public static string Begin()
    {
        var id = Mint();
        Ambient.Value = id;
        return id;
    }

    public static string Adopt(string? existing)
    {
        var id = existing ?? Mint();
        Ambient.Value = id;
        return id;
    }
}
