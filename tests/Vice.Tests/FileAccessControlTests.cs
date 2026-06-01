using Vice.Persistence;
using Xunit;

namespace Vice.Tests;

public class FileAccessControlTests
{
    [Fact]
    public void RestrictToCurrentUser_File_IsIdempotent()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "secret.txt");
        File.WriteAllText(path, "x");

        FileAccessControl.RestrictToCurrentUser(path);
        FileAccessControl.RestrictToCurrentUser(path);

        Assert.True(File.Exists(path));
        Assert.Equal("x", File.ReadAllText(path));

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public void RestrictToCurrentUser_Directory_IsIdempotent()
    {
        using var tmp = new TempDir();
        var dir = Path.Combine(tmp.Path, "vault");
        Directory.CreateDirectory(dir);

        FileAccessControl.RestrictToCurrentUser(dir);
        FileAccessControl.RestrictToCurrentUser(dir);

        Assert.True(Directory.Exists(dir));

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                         File.GetUnixFileMode(dir));
        }
    }

    [Fact]
    public void RestrictToCurrentUser_MissingPath_DoesNotThrow()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "does-not-exist");

        var ex = Record.Exception(() => FileAccessControl.RestrictToCurrentUser(path));
        Assert.Null(ex);
    }

    [UnixOnlyFact]
    public void RestrictToCurrentUser_UnixGivesUserOnlyMode()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "perm-check.txt");
        File.WriteAllText(path, "y");

        FileAccessControl.RestrictToCurrentUser(path);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            var mode = File.GetUnixFileMode(path);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }

    [UnixOnlyFact]
    public void RestrictToCurrentUser_UnixGivesUserOnlyDirMode()
    {
        using var tmp = new TempDir();
        var dir = Path.Combine(tmp.Path, "perm-dir");
        Directory.CreateDirectory(dir);

        FileAccessControl.RestrictToCurrentUser(dir);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            var mode = File.GetUnixFileMode(dir);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, mode);
        }
    }
}
