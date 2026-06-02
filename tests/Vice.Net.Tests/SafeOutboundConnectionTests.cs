using System.Net;
using System.Net.Sockets;
using CsCheck;
using Vice.Net.Requests.Grpc;
using Xunit;

namespace Vice.Net.Tests;

public class SafeOutboundConnectionTests
{
    private const long ITERATIONS = 100_000;

    private static readonly byte[] IPv4FirstOctetBoundaries =
    {
        0, 1, 9, 10, 11,
        99, 100, 127, 128,
        168, 169, 170,
        171, 172, 173,
        191, 192, 193,
        223, 224, 225,
        238, 239, 240,
        254, 255,
    };

    private static readonly byte[] ByteBoundaries =
    {
        0, 1, 2,
        15, 16, 17,
        31, 32, 33,
        63, 64, 65,
        126, 127, 128,
        167, 168, 169,
        253, 254, 255,
    };

    private static bool ExpectedIPv4(byte[] b)
    {
        if (b[0] == 127)
        {
            return true;
        }

        if (b[0] == 0
            && b[1] == 0
            && b[2] == 0
            && b[3] == 0)
        {
            return true;
        }

        if (b[0] == 10)
        {
            return true;
        }

        if (b[0] == 172
            && b[1] >= 16
            && b[1] <= 31)
        {
            return true;
        }

        if (b[0] == 192
            && b[1] == 168)
        {
            return true;
        }

        if (b[0] == 169
            && b[1] == 254)
        {
            return true;
        }

        if (b[0] == 100
            && b[1] >= 64
            && b[1] <= 127)
        {
            return true;
        }

        if (b[0] == 0)
        {
            return true;
        }

        if ((b[0] & 0xF0) == 0xE0)
        {
            return true;
        }

        if (b[0] >= 240)
        {
            return true;
        }

        return false;
    }

    [Fact]
    public void IPv4_classification_matches_reference_over_address_space()
    {
        var gen =
            from first in Gen.OneOf(Gen.Byte, Gen.OneOfConst(IPv4FirstOctetBoundaries))
            from second in Gen.OneOf(Gen.Byte, Gen.OneOfConst(ByteBoundaries))
            from third in Gen.Byte
            from fourth in Gen.Byte
            select new byte[] { first, second, third, fourth };

        gen.Sample(bytes =>
            {
                var addr = new IPAddress(bytes);
                Assert.Equal(ExpectedIPv4(bytes), SafeOutboundConnection.IsPrivateOrLocal(addr));
            },
            iter: ITERATIONS,
            seed: "0000SafeNetIPv4Cls");
    }

    [Fact]
    public void IPv4_mapped_IPv6_classifies_same_as_plain_IPv4()
    {
        var gen =
            from first in Gen.OneOf(Gen.Byte, Gen.OneOfConst(IPv4FirstOctetBoundaries))
            from rest in Gen.Byte.Array[3, 3]
            select new byte[] { first, rest[0], rest[1], rest[2] };

        gen.Sample(bytes =>
            {
                var v4 = new IPAddress(bytes);
                var mapped = v4.MapToIPv6();
                Assert.Equal(
                    SafeOutboundConnection.IsPrivateOrLocal(v4),
                    SafeOutboundConnection.IsPrivateOrLocal(mapped));
            },
            iter: ITERATIONS,
            seed: "0000SafeNetMapped0");
    }

    [Fact]
    public void IPv6_classification_is_consistent_with_dotnet_flags()
    {
        Gen.Byte.Array[16, 16].Sample(bytes =>
            {
                var addr = new IPAddress(bytes);
                if (addr.IsIPv4MappedToIPv6)
                {
                    return;
                }

                var expected = addr.IsIPv6LinkLocal
                    || addr.IsIPv6SiteLocal
                    || addr.IsIPv6Multicast
                    || IPAddress.IsLoopback(addr)
                    || IPAddress.IPv6Any.Equals(addr)
                    || (bytes[0] & 0xFE) == 0xFC;
                Assert.Equal(expected, SafeOutboundConnection.IsPrivateOrLocal(addr));
            },
            iter: ITERATIONS,
            seed: "0000SafeNetIPv6Cls");
    }

    [Fact]
    public void Blocked_IPv4_range_edges_are_classified_consistently()
    {
        Assert.False(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("9.255.255.255")));
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("10.0.0.0")));
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("10.255.255.255")));
        Assert.False(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("11.0.0.0")));

        Assert.False(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("172.15.255.255")));
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("172.16.0.0")));
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("172.31.255.255")));
        Assert.False(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("172.32.0.0")));

        Assert.False(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("100.63.255.255")));
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("100.64.0.0")));
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("100.127.255.255")));
        Assert.False(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("100.128.0.0")));

        Assert.False(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("223.255.255.255")));
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("224.0.0.0")));
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("239.255.255.255")));
        Assert.True(SafeOutboundConnection.IsPrivateOrLocal(IPAddress.Parse("240.0.0.0")));
    }

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
