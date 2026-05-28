using System.Net;
using Vice.Network.gRPC;
using Xunit;

namespace Vice.Net.Tests;

public class SafeOutboundConnectionTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.5.5.5")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    public void RejectsPrivateAndLocalIPv4(string ip)
    {
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse(ip)),
            $"{ip} should be classified private/local");
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    public void RejectsPrivateAndLocalIPv6(string ip)
    {
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse(ip)),
            $"{ip} should be classified private/local");
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.254")]
    [InlineData("100.128.0.1")]
    [InlineData("192.167.255.254")]
    [InlineData("192.169.0.1")]
    [InlineData("9.255.255.255")]
    [InlineData("11.0.0.1")]
    [InlineData("169.253.255.254")]
    [InlineData("169.255.0.1")]
    [InlineData("223.255.255.254")]
    public void AcceptsPublicIPv4(string ip)
    {
        Assert.False(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse(ip)),
            $"{ip} should be classified public");
    }

    [Theory]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:db8::1")]
    public void AcceptsPublicIPv6(string ip)
    {
        Assert.False(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse(ip)),
            $"{ip} should be classified public");
    }
}
