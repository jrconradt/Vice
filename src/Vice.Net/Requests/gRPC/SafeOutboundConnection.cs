using System.Net;
using System.Net.Sockets;

namespace Vice.Network.gRPC;

public static class SafeOutboundConnection
{
    private static readonly Lazy<SafeNetPolicy> _defaultPolicy =
        new(SafeNetPolicy.LoadDefault, LazyThreadSafetyMode.ExecutionAndPublication);

    private static SafeNetPolicy? _override;

    public static SafeNetPolicy Policy
    {
        get => Volatile.Read(ref _override) ?? _defaultPolicy.Value;
        set => Volatile.Write(ref _override, value);
    }

    public static void ResetPolicy() => Volatile.Write(ref _override, null);

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await CheckEndpointAsync(endpoint.Host, ct).ConfigureAwait(false);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static async ValueTask<IPAddress[]> CheckEndpointAsync(string host, CancellationToken ct)
    {
        var policy = Policy;
        var hostDecision = policy.EvaluateHost(host);
        if (hostDecision == SafeNetDecision.Refuse)
        {
            throw new SafeNetBlockedException(
                $"refused outbound connection to '{host}': host on deny list.");
        }

        var addresses = await ResolveAsync(host, ct).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new SafeNetBlockedException($"refused: cannot resolve host '{host}'.");
        }

        if (hostDecision == SafeNetDecision.Allow)
        {
            return addresses;
        }

        foreach (var addr in addresses)
        {
            var ipDecision = policy.EvaluateAddress(addr);
            if (ipDecision == SafeNetDecision.Refuse)
            {
                throw new SafeNetBlockedException(
                    $"refused outbound connection to '{host}': address {addr} on deny list.");
            }

            if (ipDecision == SafeNetDecision.Allow)
            {
                continue;
            }

            if (IsPrivateOrLocal(addr))
            {
                throw new SafeNetBlockedException(
                    $"refused outbound connection to '{host}': resolves to private/local address {addr}.");
            }
        }

        return addresses;
    }

    public static bool IsPrivateOrLocal(IPAddress addr)
    {
        if (IPAddress.IsLoopback(addr))
        {
            return true;
        }

        if (IPAddress.Any.Equals(addr) || IPAddress.IPv6Any.Equals(addr))
        {
            return true;
        }

        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = addr.GetAddressBytes();
            if (bytes[0] == 10)
            {
                return true;
            }

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
            {
                return true;
            }

            if (bytes[0] == 0)
            {
                return true;
            }

            if ((bytes[0] & 0xF0) == 0xE0)
            {
                return true;
            }

            if (bytes[0] >= 240)
            {
                return true;
            }

            return false;
        }

        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (addr.IsIPv6LinkLocal)
            {
                return true;
            }

            if (addr.IsIPv6SiteLocal)
            {
                return true;
            }

            if (addr.IsIPv6Multicast)
            {
                return true;
            }

            if (addr.IsIPv4MappedToIPv6)
            {
                return IsPrivateOrLocal(addr.MapToIPv4());
            }

            var bytes = addr.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }

            return false;
        }

        return false;
    }

    private static async ValueTask<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return new[] { literal };
        }

        try
        {
            return await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return Array.Empty<IPAddress>();
        }
    }
}

public sealed class SafeNetBlockedException : HttpRequestException
{
    public SafeNetBlockedException(string message) : base(message) { }
}

