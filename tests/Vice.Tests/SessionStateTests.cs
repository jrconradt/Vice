using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class SessionStateTests
{
    [Fact]
    public void CustomBasePath_UsedDirectly()
    {
        using var tmp = new TempDir();
        var state = new SessionState(tmp.Path);
        Assert.Equal(tmp.Path, state.BasePath);
        Assert.True(Directory.Exists(state.BasePath));
    }

    [Fact]
    public void DerivedPaths_CompositeWithBase()
    {
        using var tmp = new TempDir();
        var state = new SessionState(tmp.Path);
        Assert.Equal(Path.Combine(tmp.Path, "history"), state.HistoryPath);
        Assert.Equal(Path.Combine(tmp.Path, "jobs.json"), state.JobsPath);
        Assert.Equal(Path.Combine(tmp.Path, "config.json"), state.ConfigPath);
    }

    [Fact]
    public void PipeName_DefaultsToPerUser()
    {
        using var tmp = new TempDir();
        var s1 = new SessionState(tmp.Path);
        var s2 = new SessionState(tmp.Path);
        Assert.Equal(s1.PipeName, s2.PipeName);
        Assert.StartsWith("vice-session-", s1.PipeName);
        Assert.NotEqual("vice-session-", s1.PipeName);
    }

    [Fact]
    public void PipeName_OverrideRespected()
    {
        using var tmp = new TempDir();
        var s = new SessionState(tmp.Path, pipeName: "custom-pipe");
        Assert.Equal("custom-pipe", s.PipeName);
    }

    [Fact]
    public async Task Config_Get_DefaultsWhenMissing()
    {
        using var tmp = new TempDir();
        var state = new SessionState(tmp.Path);
        Assert.Equal(99, await state.GetConfigAsync("nonexistent", 99, default));
    }

    [Fact]
    public async Task Concurrency_DefaultsToThree()
    {
        using var tmp = new TempDir();
        var state = new SessionState(tmp.Path);
        Assert.Equal(3, await state.GetConcurrencyAsync(default));
    }
}
