using System.Net;
using Vice.Network.gRPC;
using Xunit;

namespace Vice.Net.Tests;

[CollectionDefinition("SafeOutboundPolicy", DisableParallelization = true)]
public sealed class SafeOutboundPolicyCollection { }

[Collection("SafeOutboundPolicy")]
public class SafeOutboundCheckEndpointTests : IDisposable
{
    public SafeOutboundCheckEndpointTests() => SafeOutboundConnection.Policy = SafeNetPolicy.Empty;
    public void Dispose() => SafeOutboundConnection.ResetPolicy();

    [Fact]
    public async Task LoopbackLiteral_IsBlocked_Default()
    {
        await Assert.ThrowsAsync<SafeNetBlockedException>(async () =>
            await SafeOutboundConnection.CheckEndpointAsync("127.0.0.1", default));
    }

    [Fact]
    public async Task PrivateRange_10_IsBlocked_Default()
    {
        await Assert.ThrowsAsync<SafeNetBlockedException>(async () =>
            await SafeOutboundConnection.CheckEndpointAsync("10.0.0.1", default));
    }

    [Fact]
    public async Task LinkLocal_IsBlocked_Default()
    {
        await Assert.ThrowsAsync<SafeNetBlockedException>(async () =>
            await SafeOutboundConnection.CheckEndpointAsync("169.254.1.1", default));
    }

    [Fact]
    public async Task PublicLiteral_IsAllowed()
    {
        var addrs = await SafeOutboundConnection.CheckEndpointAsync("8.8.8.8", default);
        Assert.NotEmpty(addrs);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), addrs[0]);
    }

    [Fact]
    public async Task ExplicitAllowOverride_AllowsLoopback()
    {
        Assert.True(IpRange.TryParse("127.0.0.0/8", out var range));
        SafeOutboundConnection.Policy = new SafeNetPolicy(
            AllowIps: new[] { range },
            DenyIps: Array.Empty<IpRange>(),
            AllowHosts: Array.Empty<HostPattern>(),
            DenyHosts: Array.Empty<HostPattern>());

        var addrs = await SafeOutboundConnection.CheckEndpointAsync("127.0.0.1", default);
        Assert.NotEmpty(addrs);
    }

    [Fact]
    public async Task DenyHostList_RejectsByName()
    {
        Assert.True(HostPattern.TryParse("evil.example.com", out var pat));
        SafeOutboundConnection.Policy = new SafeNetPolicy(
            AllowIps: Array.Empty<IpRange>(),
            DenyIps: Array.Empty<IpRange>(),
            AllowHosts: Array.Empty<HostPattern>(),
            DenyHosts: new[] { pat });

        await Assert.ThrowsAsync<SafeNetBlockedException>(async () =>
            await SafeOutboundConnection.CheckEndpointAsync("evil.example.com", default));
    }
}
