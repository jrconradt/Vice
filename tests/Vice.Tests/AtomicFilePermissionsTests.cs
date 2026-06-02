using System.Text;
using Vice.Persistence;
using Xunit;

namespace Vice.Tests;

public class AtomicFilePermissionsTests
{
    [UnixOnlyFact]
    public async Task WriteAllBytesAsync_OnUnix_CreatesFileWithUserOnlyPermissions()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "secret.bin");
        await AtomicFile.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("payload"), default);

        Assert.True(File.Exists(path));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            var mode = File.GetUnixFileMode(path);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }

    [UnixOnlyFact]
    public async Task WriteAllBytesAsync_OnUnix_CreatesParentDirectoryWith0700()
    {
        using var tmp = new TempDir();
        var nested = Path.Combine(tmp.Path, "nested-dir");
        var path = Path.Combine(nested, "leaf.bin");
        await AtomicFile.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("x"), default);

        Assert.True(Directory.Exists(nested));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            var dirMode = File.GetUnixFileMode(nested);
            var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Assert.Equal(expected, dirMode);
        }
    }

    [UnixOnlyFact]
    public async Task AppendTextAsync_OnUnix_CreatesFileWithUserOnlyPermissions()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "append.log");
        await AtomicFile.AppendTextAsync(path, "line1\n", default);

        Assert.True(File.Exists(path));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            var mode = File.GetUnixFileMode(path);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }
}
