using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class InputHistoryTests
{
    [Fact]
    public async Task Append_AccumulatesInOrder()
    {
        var h = new InputHistory();
        await h.AppendAsync("alpha", default);
        await h.AppendAsync("beta", default);

        Assert.Equal(new[] { "alpha", "beta" }, h.GetHistory());
    }

    [Fact]
    public async Task Append_SkipsBlank()
    {
        var h = new InputHistory();
        await h.AppendAsync("", default);
        await h.AppendAsync("   ", default);
        await h.AppendAsync("a", default);
        Assert.Single(h.GetHistory());
        Assert.Equal("a", h.GetHistory()[0]);
    }

    [Fact]
    public async Task Append_SkipsConsecutiveDuplicates()
    {
        var h = new InputHistory();
        await h.AppendAsync("a", default);
        await h.AppendAsync("a", default);
        await h.AppendAsync("b", default);
        await h.AppendAsync("a", default);
        Assert.Equal(new[] { "a", "b", "a" }, h.GetHistory());
    }

    [Fact]
    public async Task GetHistory_Count_ReturnsTail()
    {
        var h = new InputHistory();
        await h.AppendAsync("a", default);
        await h.AppendAsync("b", default);
        await h.AppendAsync("c", default);

        Assert.Equal(new[] { "b", "c" }, h.GetHistory(2));
        Assert.Equal(new[] { "a", "b", "c" }, h.GetHistory(99));
    }

    [Theory]
    [InlineData("foo --token=abc123", "foo --token=<redacted>")]
    [InlineData("foo --token abc123", "foo --token <redacted>")]
    [InlineData("foo --password=hunter2", "foo --password=<redacted>")]
    [InlineData("foo --api-key=xyz", "foo --api-key=<redacted>")]
    [InlineData("foo --api_key xyz", "foo --api_key <redacted>")]
    [InlineData("foo --authorization Bearer:abc", "foo --authorization <redacted>")]
    [InlineData("foo --bearer abc", "foo --bearer <redacted>")]
    [InlineData("foo --secret=s", "foo --secret=<redacted>")]
    public void Redact_FlagPatterns(string input, string expected)
    {
        Assert.Equal(expected, InputHistory.Redact(input));
    }

    [Theory]
    [InlineData("grpc call svc/m --metadata '{\"x\":\"y\"}'", "grpc call svc/m --metadata <redacted>")]
    [InlineData("http get --header 'Authorization: Bearer abc'", "http get --header <redacted>")]
    [InlineData("http get -H 'Cookie: sid=abc'", "http get -H <redacted>")]
    [InlineData("foo --cookie sessionid=zzz", "foo --cookie <redacted>")]
    [InlineData("foo --credentials u:p", "foo --credentials <redacted>")]
    public void Redact_NewFlagsAndShorthands(string input, string expected)
    {
        Assert.Equal(expected, InputHistory.Redact(input));
    }

    [Fact]
    public void Redact_JsonCredentialField_RedactsValueKeepsKey()
    {
        var input = "post body {\"authorization\":\"Bearer abc.def.ghi\",\"other\":\"keep\"}";
        var actual = InputHistory.Redact(input);
        Assert.Contains("\"authorization\":\"<redacted>\"", actual);
        Assert.Contains("\"other\":\"keep\"", actual);
    }

    [Theory]
    [InlineData("\"api_key\":\"abc\"", "\"api_key\":\"<redacted>\"")]
    [InlineData("\"api-key\":\"abc\"", "\"api-key\":\"<redacted>\"")]
    [InlineData("\"x-api-key\":\"abc\"", "\"x-api-key\":\"<redacted>\"")]
    [InlineData("\"x-auth-token\":\"abc\"", "\"x-auth-token\":\"<redacted>\"")]
    [InlineData("\"password\":\"hunter2\"", "\"password\":\"<redacted>\"")]
    [InlineData("\"set-cookie\":\"sid=abc\"", "\"set-cookie\":\"<redacted>\"")]
    [InlineData("\"token\":\"abc\"", "\"token\":\"<redacted>\"")]
    [InlineData("\"bearer\":\"abc\"", "\"bearer\":\"<redacted>\"")]
    public void Redact_JsonField_AllCredentialKeys(string input, string expected)
    {
        Assert.Equal(expected, InputHistory.Redact(input));
    }

    [Fact]
    public void Redact_NonCredentialJsonField_LeftAlone()
    {
        var input = "{\"name\":\"alice\",\"age\":\"30\"}";
        Assert.Equal(input, InputHistory.Redact(input));
    }

    [Fact]
    public void Redact_NoMatch_ReturnsUnchanged()
    {
        Assert.Equal("plain command", InputHistory.Redact("plain command"));
    }
}
