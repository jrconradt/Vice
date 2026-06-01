using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class SessionStateTests
{
    [Fact]
    public void PipeName_DefaultsToPerUser()
    {
        var s1 = new SessionState("vice-test");
        var s2 = new SessionState("vice-test");
        Assert.Equal(s1.PipeName, s2.PipeName);
        Assert.StartsWith("vice-test-session-", s1.PipeName);
        Assert.NotEqual("vice-test-session-", s1.PipeName);
    }

    [Fact]
    public void PipeName_OverrideRespected()
    {
        var s = new SessionState("vice-test", pipeName: "custom-pipe");
        Assert.Equal("custom-pipe", s.PipeName);
    }
}
