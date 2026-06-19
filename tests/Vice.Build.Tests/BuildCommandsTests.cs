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
}
