using System.Net;
using Vice.Network.gRPC;
using Xunit;

namespace Vice.Net.Tests;

public class SafeNetPolicyTests
{
    [Theory]
    [InlineData("128.0.0.0/1", "200.0.0.0", true)]
    [InlineData("128.0.0.0/1", "127.255.255.255", false)]
    [InlineData("254.0.0.0/7", "255.0.0.0", true)]
    [InlineData("254.0.0.0/7", "252.0.0.0", false)]
    [InlineData("10.128.0.0/9", "10.200.0.0", true)]
    [InlineData("10.128.0.0/9", "10.100.0.0", false)]
    [InlineData("10.128.0.0/9", "11.128.0.0", false)]
    [InlineData("10.10.2.0/23", "10.10.3.5", true)]
    [InlineData("10.10.2.0/23", "10.10.4.0", false)]
    [InlineData("192.168.1.0/25", "192.168.1.100", true)]
    [InlineData("192.168.1.0/25", "192.168.1.200", false)]
    [InlineData("192.168.1.4/31", "192.168.1.5", true)]
    [InlineData("192.168.1.4/31", "192.168.1.6", false)]
    public void Contains_NonByteAlignedPrefix_PinsMaskArithmetic(
        string cidr,
        string candidate,
        bool expected)
    {
        Assert.True(IpRange.TryParse(cidr, out var range));
        Assert.Equal(expected, range.Contains(IPAddress.Parse(candidate)));
    }

    [Theory]
    [InlineData("2001:db8::/32", "2001:db8:1::1", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    public void Contains_IPv6Prefix(string cidr, string candidate, bool expected)
    {
        Assert.True(IpRange.TryParse(cidr, out var range));
        Assert.Equal(expected, range.Contains(IPAddress.Parse(candidate)));
    }

    [Fact]
    public void Contains_FamilyMismatch_ReturnsFalse()
    {
        Assert.True(IpRange.TryParse("2001:db8::/32", out var v6Range));
        Assert.False(v6Range.Contains(IPAddress.Parse("8.8.8.8")));

        Assert.True(IpRange.TryParse("10.0.0.0/8", out var v4Range));
        Assert.False(v4Range.Contains(IPAddress.Parse("::1")));
    }

    [Theory]
    [InlineData("1.2.3.4/40")]
    [InlineData("1.2.3.4/33")]
    [InlineData("1.2.3.4/x")]
    [InlineData("1.2.3.4/")]
    [InlineData("not-an-address")]
    public void TryParse_RejectsInvalid(string text)
    {
        Assert.False(IpRange.TryParse(text, out _));
    }

    [Fact]
    public void TryParse_SelectsMaxPrefixByFamily()
    {
        Assert.True(IpRange.TryParse("1.2.3.4/32", out var v4));
        Assert.Equal(32, v4.PrefixLength);
        Assert.False(IpRange.TryParse("1.2.3.4/33", out _));

        Assert.True(IpRange.TryParse("2001:db8::1/128", out var v6));
        Assert.Equal(128, v6.PrefixLength);
        Assert.False(IpRange.TryParse("2001:db8::1/129", out _));
    }

    [Theory]
    [InlineData("a.example.com", true)]
    [InlineData("deep.sub.example.com", true)]
    [InlineData("A.EXAMPLE.COM", true)]
    [InlineData("example.com", false)]
    [InlineData("notexample.com", false)]
    [InlineData("example.com.evil.net", false)]
    public void HostPattern_Wildcard_Matches(string host, bool expected)
    {
        Assert.True(HostPattern.TryParse("*.example.com", out var pat));
        Assert.True(pat.IsWildcard);
        Assert.Equal(expected, pat.Matches(host));
    }

    [Theory]
    [InlineData("evil.example.com", true)]
    [InlineData("EVIL.EXAMPLE.COM", true)]
    [InlineData("a.evil.example.com", false)]
    [InlineData("example.com", false)]
    public void HostPattern_Exact_Matches(string host, bool expected)
    {
        Assert.True(HostPattern.TryParse("evil.example.com", out var pat));
        Assert.False(pat.IsWildcard);
        Assert.Equal(expected, pat.Matches(host));
    }

    [Theory]
    [InlineData("*.x")]
    [InlineData("a*b")]
    [InlineData("*.a*b.com")]
    public void HostPattern_TryParse_RejectsInvalid(string text)
    {
        Assert.False(HostPattern.TryParse(text, out _));
    }

    [Fact]
    public void EvaluateAddress_DenyBeforeAllow()
    {
        Assert.True(IpRange.TryParse("10.0.0.0/8", out var allow));
        Assert.True(IpRange.TryParse("10.1.0.0/16", out var deny));
        var policy = new SafeNetPolicy(
            AllowIps: new[] { allow },
            DenyIps: new[] { deny },
            AllowHosts: Array.Empty<HostPattern>(),
            DenyHosts: Array.Empty<HostPattern>());

        Assert.Equal(SafeNetDecision.Refuse, policy.EvaluateAddress(IPAddress.Parse("10.1.2.3")));
        Assert.Equal(SafeNetDecision.Allow, policy.EvaluateAddress(IPAddress.Parse("10.2.2.3")));
        Assert.Equal(SafeNetDecision.Default, policy.EvaluateAddress(IPAddress.Parse("11.0.0.1")));
    }

    [Fact]
    public void EvaluateHost_DenyBeforeAllow()
    {
        Assert.True(HostPattern.TryParse("*.example.com", out var allow));
        Assert.True(HostPattern.TryParse("evil.example.com", out var deny));
        var policy = new SafeNetPolicy(
            AllowIps: Array.Empty<IpRange>(),
            DenyIps: Array.Empty<IpRange>(),
            AllowHosts: new[] { allow },
            DenyHosts: new[] { deny });

        Assert.Equal(SafeNetDecision.Refuse, policy.EvaluateHost("evil.example.com"));
        Assert.Equal(SafeNetDecision.Allow, policy.EvaluateHost("good.example.com"));
        Assert.Equal(SafeNetDecision.Default, policy.EvaluateHost("example.com"));
    }
}
