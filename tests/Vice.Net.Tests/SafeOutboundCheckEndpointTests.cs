using System.Net;
using Vice.Net.Requests.Grpc;
using Xunit;

namespace Vice.Net.Tests;

public class SafeOutboundCheckEndpointTests
{
    [Fact]
    public async Task LoopbackLiteral_IsBlocked_UnderEmptyPolicy()
    {
        await Assert.ThrowsAsync<SafeNetBlockedException>(async () =>
            await SafeOutboundConnection.CheckEndpointAsync("127.0.0.1", default, policy: SafeNetPolicy.Empty));
    }

    [Fact]
    public async Task PrivateRange_10_IsBlocked_UnderEmptyPolicy()
    {
        await Assert.ThrowsAsync<SafeNetBlockedException>(async () =>
            await SafeOutboundConnection.CheckEndpointAsync("10.0.0.1", default, policy: SafeNetPolicy.Empty));
    }

    [Fact]
    public async Task LinkLocal_IsBlocked_UnderEmptyPolicy()
    {
        await Assert.ThrowsAsync<SafeNetBlockedException>(async () =>
            await SafeOutboundConnection.CheckEndpointAsync("169.254.1.1", default, policy: SafeNetPolicy.Empty));
    }

    [Fact]
    public async Task PublicLiteral_IsAllowed()
    {
        var addrs = await SafeOutboundConnection.CheckEndpointAsync("8.8.8.8", default, policy: SafeNetPolicy.Empty);
        Assert.NotEmpty(addrs);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), addrs[0]);
    }

    [Fact]
    public async Task ExplicitAllow_AllowsLoopback()
    {
        Assert.True(IpRange.TryParse("127.0.0.0/8", out var range));
        var policy = new SafeNetPolicy(
            AllowIps: new[] { range },
            DenyIps: Array.Empty<IpRange>(),
            AllowHosts: Array.Empty<HostPattern>(),
            DenyHosts: Array.Empty<HostPattern>());

        var addrs = await SafeOutboundConnection.CheckEndpointAsync("127.0.0.1", default, policy: policy);
        Assert.NotEmpty(addrs);
    }

    [Fact]
    public async Task DenyHostList_RejectsByName()
    {
        Assert.True(HostPattern.TryParse("evil.example.com", out var pat));
        var policy = new SafeNetPolicy(
            AllowIps: Array.Empty<IpRange>(),
            DenyIps: Array.Empty<IpRange>(),
            AllowHosts: Array.Empty<HostPattern>(),
            DenyHosts: new[] { pat });

        await Assert.ThrowsAsync<SafeNetBlockedException>(async () =>
            await SafeOutboundConnection.CheckEndpointAsync("evil.example.com", default, policy: policy));
    }
}
