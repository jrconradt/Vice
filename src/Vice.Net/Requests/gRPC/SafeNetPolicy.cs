using System.Net;
using System.Net.Sockets;
using Vice.Logging;

namespace Vice.Net.Requests.Grpc;

public sealed record SafeNetPolicy(
    IReadOnlyList<IpRange> AllowIps,
    IReadOnlyList<IpRange> DenyIps,
    IReadOnlyList<HostPattern> AllowHosts,
    IReadOnlyList<HostPattern> DenyHosts)
{
    public const string AllowIpsEnvVar = "VICE_SAFE_NET_ALLOW";
    public const string DenyIpsEnvVar = "VICE_SAFE_NET_DENY";
    public const string AllowHostsEnvVar = "VICE_SAFE_NET_ALLOW_HOSTS";
    public const string DenyHostsEnvVar = "VICE_SAFE_NET_DENY_HOSTS";

    public static readonly SafeNetPolicy Empty = new(
        Array.Empty<IpRange>(), Array.Empty<IpRange>(),
        Array.Empty<HostPattern>(), Array.Empty<HostPattern>());

    public SafeNetDecision EvaluateHost(string host)
    {
        foreach (var p in DenyHosts)
        {
            if (p.Matches(host))
            {
                return SafeNetDecision.Refuse;
            }
        }

        foreach (var p in AllowHosts)
        {
            if (p.Matches(host))
            {
                return SafeNetDecision.Allow;
            }
        }

        return SafeNetDecision.Default;
    }

    public SafeNetDecision EvaluateAddress(IPAddress address)
    {
        foreach (var r in DenyIps)
        {
            if (r.Contains(address))
            {
                return SafeNetDecision.Refuse;
            }
        }

        foreach (var r in AllowIps)
        {
            if (r.Contains(address))
            {
                return SafeNetDecision.Allow;
            }
        }

        return SafeNetDecision.Default;
    }

    public static SafeNetPolicy Combine(params SafeNetPolicy[] sources)
    {
        var allowIps = new List<IpRange>();
        var denyIps = new List<IpRange>();
        var allowHosts = new List<HostPattern>();
        var denyHosts = new List<HostPattern>();
        foreach (var s in sources)
        {
            allowIps.AddRange(s.AllowIps);
            denyIps.AddRange(s.DenyIps);
            allowHosts.AddRange(s.AllowHosts);
            denyHosts.AddRange(s.DenyHosts);
        }
        return new SafeNetPolicy(allowIps, denyIps, allowHosts, denyHosts);
    }

    public static SafeNetPolicy LoadDefault()
    {
        return FromEnvironment();
    }

    public static SafeNetPolicy FromEnvironment(IViceLogger? logger = null)
    {
        var sink = logger ?? NullViceLogger.Instance;
        return new(
            ParseIpList(Environment.GetEnvironmentVariable(AllowIpsEnvVar), AllowIpsEnvVar, strictDeny: false, sink),
            ParseIpList(Environment.GetEnvironmentVariable(DenyIpsEnvVar), DenyIpsEnvVar, strictDeny: true, sink),
            ParseHostList(Environment.GetEnvironmentVariable(AllowHostsEnvVar), AllowHostsEnvVar, strictDeny: false, sink),
            ParseHostList(Environment.GetEnvironmentVariable(DenyHostsEnvVar), DenyHostsEnvVar, strictDeny: true, sink));
    }

    private static List<IpRange> ParseIpList(string? raw,
                                             string envVar,
                                             bool strictDeny,
                                             IViceLogger logger)
    {
        var result = new List<IpRange>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        foreach (var part in raw.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = part.Trim();
            if (IpRange.TryParse(token, out var range))
            {
                result.Add(range);
            }
            else if (strictDeny)
            {
                throw new SafeNetPolicyException(
                    $"SafeNet: unparsable IP range '{token}' in deny list {envVar}; refusing to load policy because a discarded deny rule would fail open.");
            }
            else
            {
                logger.Log(
                    ViceLogLevel.Warn,
                    $"SafeNet: ignoring unparsable IP range '{token}' from {envVar}; this entry will not be enforced.");
            }
        }
        return result;
    }

    private static List<HostPattern> ParseHostList(string? raw,
                                                   string envVar,
                                                   bool strictDeny,
                                                   IViceLogger logger)
    {
        var result = new List<HostPattern>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        foreach (var part in raw.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = part.Trim();
            if (HostPattern.TryParse(token, out var pat))
            {
                result.Add(pat);
            }
            else if (strictDeny)
            {
                throw new SafeNetPolicyException(
                    $"SafeNet: unparsable host pattern '{token}' in deny list {envVar}; refusing to load policy because a discarded deny rule would fail open.");
            }
            else
            {
                logger.Log(
                    ViceLogLevel.Warn,
                    $"SafeNet: ignoring unparsable host pattern '{token}' from {envVar}; this entry will not be enforced.");
            }
        }
        return result;
    }

}

public sealed class SafeNetPolicyException : Exception
{
    public SafeNetPolicyException(string message) : base(message) { }
}

public enum SafeNetDecision
{
    Default,
    Allow,
    Refuse,
}

public sealed record IpRange(IPAddress Address, int PrefixLength)
{
    public bool Contains(IPAddress addr)
    {
        if (addr.AddressFamily != Address.AddressFamily)
        {
            return false;
        }

        var target = addr.GetAddressBytes();
        var network = Address.GetAddressBytes();
        var bits = PrefixLength;
        var byteIndex = 0;

        while (bits >= 8)
        {
            if (target[byteIndex] != network[byteIndex])
            {
                return false;
            }

            bits -= 8;
            byteIndex++;
        }

        if (bits > 0)
        {
            var mask = (byte)(0xFF << (8 - bits));
            if ((target[byteIndex] & mask) != (network[byteIndex] & mask))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryParse(string text, out IpRange range)
    {
        range = default!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var slash = text.IndexOf('/');
        string addrPart;
        int prefix;

        if (slash >= 0)
        {
            addrPart = text[..slash];
            if (!int.TryParse(text[(slash + 1)..], out prefix))
            {
                return false;
            }
        }
        else
        {
            addrPart = text;
            prefix = -1;
        }

        if (!IPAddress.TryParse(addrPart, out var addr))
        {
            return false;
        }

        var maxPrefix = addr.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefix < 0)
        {
            prefix = maxPrefix;
        }
        else if (prefix > maxPrefix)
        {
            return false;
        }

        range = new IpRange(addr, prefix);
        return true;
    }
}

public sealed record HostPattern(string Value, bool IsWildcard)
{
    public bool Matches(string host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        if (IsWildcard)
        {
            return host.EndsWith(Value, StringComparison.OrdinalIgnoreCase)
                   && host.Length > Value.Length;
        }

        return string.Equals(host, Value, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(string text, out HostPattern pattern)
    {
        pattern = default!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = text[1..];
            var domain = text[2..];
            if (suffix.Contains('*')
                || domain.Length == 0
                || !domain.Contains('.'))
            {
                return false;
            }

            pattern = new HostPattern(suffix, IsWildcard: true);
            return true;
        }

        if (text.Contains('*'))
        {
            return false;
        }

        pattern = new HostPattern(text, IsWildcard: false);
        return true;
    }
}
