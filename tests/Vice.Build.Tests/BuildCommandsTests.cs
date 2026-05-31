using Vice.Build;
using Xunit;

namespace Vice.Build.Tests;

public class BuildCommandsTests
{
    [Fact]
    public void CanonicalPath_NullRequested_UsesCurrentDirectory()
    {
        var expected = Path.GetFullPath(Directory.GetCurrentDirectory());
        Assert.Equal(expected, BuildCommands.CanonicalPath(null));
    }

    [Fact]
    public void CanonicalPath_RelativeRequested_IsCanonicalized()
    {
        var canonical = BuildCommands.CanonicalPath(".");
        Assert.True(Path.IsPathRooted(canonical));
        Assert.Equal(Path.GetFullPath("."), canonical);
    }

    [Fact]
    public void BuildKey_CombinesVerbAndCanonicalPath()
    {
        var canonical = BuildCommands.CanonicalPath("/tmp/project");
        Assert.Equal($"build::{canonical}", BuildCommands.BuildKey("build", canonical));
    }

    [Fact]
    public void BuildKey_SameVerbDifferentRequestForms_ProduceSameKey()
    {
        var current = Directory.GetCurrentDirectory();
        var a = BuildCommands.BuildKey("test", BuildCommands.CanonicalPath(current));
        var b = BuildCommands.BuildKey("test", BuildCommands.CanonicalPath("."));
        Assert.Equal(a, b);
    }

    [Fact]
    public void BuildKey_DifferentVerbsSamePath_ProduceDistinctKeys()
    {
        var canonical = BuildCommands.CanonicalPath("/tmp/project");
        var build = BuildCommands.BuildKey("build", canonical);
        var test = BuildCommands.BuildKey("test", canonical);
        Assert.NotEqual(build, test);
    }
}
