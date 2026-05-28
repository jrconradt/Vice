using System.Text;
using Vice.Persistence;
using Xunit;

namespace Vice.Tests;

public class AtomicFilePermissionsTests
{
    [Fact]
    public async Task WriteAllBytesAsync_OnUnix_CreatesFileWithUserOnlyPermissions()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
        {
            return;
        }

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "secret.bin");
        await AtomicFile.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("payload"), default);

        Assert.True(File.Exists(path));
        var mode = File.GetUnixFileMode(path);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public async Task WriteAllBytesAsync_OnUnix_CreatesParentDirectoryWith0700()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
        {
            return;
        }

        using var tmp = new TempDir();
        var nested = Path.Combine(tmp.Path, "nested-dir");
        var path = Path.Combine(nested, "leaf.bin");
        await AtomicFile.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("x"), default);

        Assert.True(Directory.Exists(nested));
        var dirMode = File.GetUnixFileMode(nested);
        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.Equal(expected, dirMode);
    }

    [Fact]
    public async Task AppendTextAsync_OnUnix_CreatesFileWithUserOnlyPermissions()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
        {
            return;
        }

        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "append.log");
        await AtomicFile.AppendTextAsync(path, "line1\n", default);

        Assert.True(File.Exists(path));
        var mode = File.GetUnixFileMode(path);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }
}
