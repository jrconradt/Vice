using System.Text;
using Vice.Display;
using Vice.Execution;
using Vice.Net.Commands.Network;
using Xunit;

namespace Vice.Net.Tests;

public class NetworkOptionsTests
{
    private static CommandContext CtxWith(params (string key, string? value)[] options)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (k, v) in options)
        {
            dict[k] = v;
        }

        return CommandContextFactory.Build(new RecordingConsole(), globalOptions: dict);
    }

    [Fact]
    public void ParseEndpoint_splits_host_and_port()
    {
        var (host, port) = NetworkOptions.ParseEndpoint("example.com:8080");
        Assert.Equal("example.com", host);
        Assert.Equal(8080, port);
    }

    [Theory]
    [InlineData("missingport")]
    [InlineData("host:notanumber")]
    [InlineData("host:0")]
    [InlineData("host:99999")]
    [InlineData("host:-1")]
    public void ParseEndpoint_rejects_invalid(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => NetworkOptions.ParseEndpoint(endpoint));
    }

    [Fact]
    public void GetTimeout_returns_default_when_unset()
    {
        var ctx = CtxWith();
        Assert.Equal(5000, NetworkOptions.GetTimeout(ctx, 5000));
    }

    [Fact]
    public void GetTimeout_parses_explicit_value()
    {
        var ctx = CtxWith(("timeout", "1234"));
        Assert.Equal(1234, NetworkOptions.GetTimeout(ctx, 5000));
    }

    [Fact]
    public void GetTimeout_throws_on_negative_or_zero()
    {
        var ctx = CtxWith(("timeout", "0"));
        Assert.Throws<ArgumentException>(() => NetworkOptions.GetTimeout(ctx, 5000));
    }

    [Theory]
    [InlineData(null, NetworkOutputFormat.Text)]
    [InlineData("text", NetworkOutputFormat.Text)]
    [InlineData("HEX", NetworkOutputFormat.Hex)]
    [InlineData("json", NetworkOutputFormat.Json)]
    public void GetFormat_accepts_known(string? raw, NetworkOutputFormat expected)
    {
        var ctx = raw is null ? CtxWith() : CtxWith(("format", raw));
        Assert.Equal(expected, NetworkOptions.GetFormat(ctx));
    }

    [Fact]
    public void GetFormat_rejects_unknown()
    {
        var ctx = CtxWith(("format", "binary"));
        Assert.Throws<ArgumentException>(() => NetworkOptions.GetFormat(ctx));
    }

    [Theory]
    [InlineData(null, "utf-8")]
    [InlineData("utf8", "utf-8")]
    [InlineData("ascii", "us-ascii")]
    public void GetEncoding_returns_correct_encoding(string? raw, string expectedWebName)
    {
        var ctx = raw is null ? CtxWith() : CtxWith(("encoding", raw));
        var enc = NetworkOptions.GetEncoding(ctx);
        Assert.Equal(expectedWebName, enc.WebName);
    }

    [Fact]
    public void GetEncoding_rejects_unknown()
    {
        var ctx = CtxWith(("encoding", "ebcdic"));
        Assert.Throws<ArgumentException>(() => NetworkOptions.GetEncoding(ctx));
    }
}
