using Vice.Contracts;
using Vice.Execution;
using Vice.Logging;

namespace Vice.Net.Research;

internal readonly record struct ResearchPaging(int Limit,
                                               int Offset);

internal static class ResearchOptions
{
    private const int DefaultLimit = 10;
    private const int MaxLimit = 100;
    private const int DefaultTimeoutMs = 30000;

    public static ResearchPaging GetPaging(ICommandContext ctx)
    {
        var limit = ParsePositive(ctx.GetGlobalOption("limit"), "limit") ?? DefaultLimit;
        if (limit > MaxLimit)
        {
            limit = MaxLimit;
        }

        var offsetValue = ctx.GetGlobalOption("offset");
        int offset;
        if (offsetValue is not null)
        {
            offset = ParseNonNegative(offsetValue, "offset");
        }
        else
        {
            var page = ParsePositive(ctx.GetGlobalOption("page"), "page");
            offset = page is { } p ? (p - 1) * limit : 0;
        }

        return new ResearchPaging(limit, offset);
    }

    public static string? GetFormat(ICommandContext ctx)
    {
        var value = ctx.GetGlobalOption("format");
        if (value is null
            || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "text", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value.ToLowerInvariant();
    }

    public static TimeSpan GetTimeout(ICommandContext ctx)
    {
        var value = ctx.GetGlobalOption("timeout");
        if (value is null)
        {
            return TimeSpan.FromMilliseconds(DefaultTimeoutMs);
        }

        if (!int.TryParse(value, out var ms) || ms <= 0)
        {
            throw new BadArgument($"Invalid timeout value '{value}'; expected positive milliseconds.");
        }

        return TimeSpan.FromMilliseconds(ms);
    }

    private static int? ParsePositive(string? value,
                                      string name)
    {
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(value, out var n) || n <= 0)
        {
            throw new BadArgument($"Invalid --{name} value '{value}'; expected a positive integer.");
        }

        return n;
    }

    private static int ParseNonNegative(string value,
                                        string name)
    {
        if (!int.TryParse(value, out var n) || n < 0)
        {
            throw new BadArgument($"Invalid --{name} value '{value}'; expected a non-negative integer.");
        }

        return n;
    }
}
