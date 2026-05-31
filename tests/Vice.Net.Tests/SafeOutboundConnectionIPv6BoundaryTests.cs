using System.Net;
using Vice.Network.gRPC;
using Xunit;

namespace Vice.Net.Tests;

public class SafeOutboundConnectionIPv6BoundaryTests
{
    [Theory]
    [InlineData("fc00::1")]
    [InlineData("fc00::")]
    [InlineData("fcff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    [InlineData("fd00::1")]
    [InlineData("fd00::")]
    [InlineData("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    public void UlaRange_IsPrivate(string ip)
    {
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse(ip)),
            $"{ip} is inside fc00::/7 and should be classified private/local");
    }

    [Theory]
    [InlineData("fb00::")]
    [InlineData("fbff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    [InlineData("fe00::")]
    public void UlaBoundaryNeighbors_ArePublic(string ip)
    {
        Assert.False(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse(ip)),
            $"{ip} is outside fc00::/7 and should be classified public");
    }
}
