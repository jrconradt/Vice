using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vice.Configuration;
using Vice.Logging;

namespace Vice.Network.gRPC;

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
    public const string SettingsFileName = "safenet.json";

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

    private sealed record DefaultCacheEntry(string Key, SafeNetPolicy Policy);

    private static readonly object _defaultCacheLock = new();
    private static DefaultCacheEntry? _defaultCacheEntry;

    public static SafeNetPolicy LoadDefault()
    {
        var path = ResolveSettingsPath();
        var key = ComputeDefaultCacheKey(path);

        var cached = Volatile.Read(ref _defaultCacheEntry);
        if (cached is not null && string.Equals(cached.Key, key, StringComparison.Ordinal))
        {
            return cached.Policy;
        }

        lock (_defaultCacheLock)
        {
            var current = Volatile.Read(ref _defaultCacheEntry);
            if (current is not null && string.Equals(current.Key, key, StringComparison.Ordinal))
            {
                return current.Policy;
            }

            var fresh = Combine(FromEnvironment(), FromFile(path));
            Volatile.Write(ref _defaultCacheEntry, new DefaultCacheEntry(key, fresh));
            return fresh;
        }
    }

    private static string ComputeDefaultCacheKey(string? path)
    {
        var mtimeTicks = 0L;
        var exists = false;
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    exists = true;
                    mtimeTicks = info.LastWriteTimeUtc.Ticks;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var allow = Environment.GetEnvironmentVariable(AllowIpsEnvVar) ?? "";
        var deny = Environment.GetEnvironmentVariable(DenyIpsEnvVar) ?? "";
        var allowHosts = Environment.GetEnvironmentVariable(AllowHostsEnvVar) ?? "";
        var denyHosts = Environment.GetEnvironmentVariable(DenyHostsEnvVar) ?? "";
        return $"{path ?? ""}|{exists}|{mtimeTicks}|{allow}|{deny}|{allowHosts}|{denyHosts}";
    }

    public static SafeNetPolicy FromEnvironment()
        => new(
            ParseIpList(Environment.GetEnvironmentVariable(AllowIpsEnvVar), AllowIpsEnvVar),
            ParseIpList(Environment.GetEnvironmentVariable(DenyIpsEnvVar), DenyIpsEnvVar),
            ParseHostList(Environment.GetEnvironmentVariable(AllowHostsEnvVar), AllowHostsEnvVar),
            ParseHostList(Environment.GetEnvironmentVariable(DenyHostsEnvVar), DenyHostsEnvVar));

    public static SafeNetPolicy FromFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize(json, SafeNetPolicyJsonContext.Default.SafeNetPolicyDocument);
            if (doc is null)
            {
                return Empty;
            }

            var rejected = new List<string>();
            var policy = new SafeNetPolicy(
                ParseIpList(doc.AllowIps, "allow", rejected),
                ParseIpList(doc.DenyIps, "deny", rejected),
                ParseHostList(doc.AllowHosts, "allow_hosts", rejected),
                ParseHostList(doc.DenyHosts, "deny_hosts", rejected));

            if (rejected.Count > 0)
            {
                throw new InvalidOperationException(
                    $"SafeNet policy file '{path}' contains {rejected.Count} unparsable entr{(rejected.Count == 1 ? "y" : "ies")}: {string.Join(", ", rejected)}; refusing to proceed with a partially-applied network policy.");
            }

            return policy;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"SafeNet policy file '{path}' is corrupt and cannot be parsed; refusing to proceed without a valid network policy.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"SafeNet policy file '{path}' could not be read; refusing to proceed without a valid network policy.", ex);
        }
    }

    public static string? ResolveSettingsPath()
    {
        var configDir = new ViceDirectories("vice").ConfigDir;
        if (string.IsNullOrEmpty(configDir))
        {
            return null;
        }

        return Path.Combine(configDir, SettingsFileName);
    }

    private static List<IpRange> ParseIpList(string? raw, string envVar)
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
            else
            {
                Vice.Log.Emit(
                    ViceLogLevel.Warn,
                    $"SafeNet: ignoring unparsable IP range '{token}' from {envVar}; this entry will not be enforced.");
            }
        }
        return result;
    }

    private static List<IpRange> ParseIpList(IReadOnlyList<string>? items, string field, List<string> rejected)
    {
        var result = new List<IpRange>();
        if (items is null)
        {
            return result;
        }

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var token = item.Trim();
            if (IpRange.TryParse(token, out var range))
            {
                result.Add(range);
            }
            else
            {
                rejected.Add($"{field}: '{token}'");
            }
        }
        return result;
    }

    private static List<HostPattern> ParseHostList(string? raw, string envVar)
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
            else
            {
                Vice.Log.Emit(
                    ViceLogLevel.Warn,
                    $"SafeNet: ignoring unparsable host pattern '{token}' from {envVar}; this entry will not be enforced.");
            }
        }
        return result;
    }

    private static List<HostPattern> ParseHostList(IReadOnlyList<string>? items, string field, List<string> rejected)
    {
        var result = new List<HostPattern>();
        if (items is null)
        {
            return result;
        }

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var token = item.Trim();
            if (HostPattern.TryParse(token, out var pat))
            {
                result.Add(pat);
            }
            else
            {
                rejected.Add($"{field}: '{token}'");
            }
        }
        return result;
    }
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

public sealed class SafeNetPolicyDocument
{
    [JsonPropertyName("allow")] public List<string>? AllowIps { get; set; }
    [JsonPropertyName("deny")] public List<string>? DenyIps { get; set; }
    [JsonPropertyName("allow_hosts")] public List<string>? AllowHosts { get; set; }
    [JsonPropertyName("deny_hosts")] public List<string>? DenyHosts { get; set; }
}

[JsonSerializable(typeof(SafeNetPolicyDocument))]
internal partial class SafeNetPolicyJsonContext : JsonSerializerContext { }
