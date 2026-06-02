using System.Text;
using Vice.Contracts;
using Vice.Display;
using Vice.Execution;
using Vice.Logging;

namespace Vice.Net.Commands.Network;

internal static class NetworkOptions
{
    public static (string host, int port) ParseEndpoint(string endpoint)
    {
        var parts = endpoint.Split(':', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])
            || !int.TryParse(parts[1], out var port)
            || port < 1
            || port > 65535)
        {
            throw new ArgumentException($"Invalid endpoint '{endpoint}'. Expected format: host:port");
        }

        return (parts[0], port);
    }

    public static int GetTimeout(ICommandContext ctx)
        => GetTimeout(ctx, NetworkConstants.DEFAULT_TIMEOUT_MS);

    public static int GetTimeout(ICommandContext ctx, int defaultMs)
    {
        var value = ctx.GetGlobalOption("timeout");
        if (value is null)
        {
            return defaultMs;
        }

        if (!int.TryParse(value, out var ms) || ms <= 0)
        {
            throw new BadArgument($"Invalid timeout value: '{value}'");
        }

        return ms;
    }

    public static TimeSpan GetTimeoutSpan(ICommandContext ctx, int defaultMs)
        => TimeSpan.FromMilliseconds(GetTimeout(ctx, defaultMs));

    public static OutputFormatKind GetFormat(CommandContext ctx)
    {
        var value = ctx.GetGlobalOption("format");
        return value?.ToLowerInvariant() switch
        {
            null or "text" => OutputFormatKind.Text,
            "hex" => OutputFormatKind.Hex,
            "json" => OutputFormatKind.Json,
            _ => throw new ArgumentException($"Invalid format: '{value}'. Expected: text, hex, or json")
        };
    }

    public static Encoding GetEncoding(CommandContext ctx)
    {
        var value = ctx.GetGlobalOption("encoding");
        return value?.ToLowerInvariant() switch
        {
            null or "utf8" => Encoding.UTF8,
            "ascii" => Encoding.ASCII,
            _ => throw new ArgumentException($"Invalid encoding: '{value}'. Expected: utf8 or ascii")
        };
    }
}
