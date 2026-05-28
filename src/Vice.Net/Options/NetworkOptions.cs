using System.Text;
using Vice.Display;
using Vice.Execution;

namespace Vice.Net.Commands.Network;

internal static class NetworkOptions
{
    public static (string host, int port) ParseEndpoint(string endpoint)
    {
        var parts = endpoint.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port) || port < 1 || port > 65535)
        {
            throw new ArgumentException($"Invalid endpoint '{endpoint}'. Expected format: host:port");
        }

        return (parts[0], port);
    }

    public static int GetTimeout(CommandContext ctx, int defaultMs)
    {
        var value = ctx.GetGlobalOption("timeout");
        if (value is null)
        {
            return defaultMs;
        }

        if (!int.TryParse(value, out var ms) || ms <= 0)
        {
            throw new ArgumentException($"Invalid timeout value: '{value}'");
        }

        return ms;
    }

    public static NetworkOutputFormat GetFormat(CommandContext ctx)
    {
        var value = ctx.GetGlobalOption("format");
        return value?.ToLowerInvariant() switch
        {
            null or "text" => NetworkOutputFormat.Text,
            "hex" => NetworkOutputFormat.Hex,
            "json" => NetworkOutputFormat.Json,
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
