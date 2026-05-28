namespace Vice.Execution;

public static class CommandContextExtensions
{
    public static string GetFullPath(this CommandContext ctx, string targetName = "path")
    {
        var value = ctx[targetName];
        if (value is null)
        {
            throw new InvalidOperationException($"Target '{targetName}' not bound.");
        }
        return Path.GetFullPath(value);
    }

    public static int? AsPositiveInt(this string? value)
        => (value is not null && int.TryParse(value, out var n) && n > 0) ? n : null;
}
