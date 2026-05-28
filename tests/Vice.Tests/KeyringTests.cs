using System.Threading.Tasks;
using Vice.Configuration;
using Xunit;

namespace Vice.Tests;

public class KeyringTests
{
    static KeyringTests()
        => Environment.SetEnvironmentVariable(FileKeyring.OptInEnvVar, "1");


    [Fact]
    public async Task NullKeyring_AllOperationsAreNoOps()
    {
        var k = NullKeyring.Instance;
        Assert.Null(await k.GetAsync("foo"));
        await k.SetAsync("foo", "bar");
        Assert.Null(await k.GetAsync("foo"));
        Assert.False(await k.DeleteAsync("foo"));
        Assert.Empty(await k.ListKeysAsync());
    }

    [Fact]
    public async Task FileKeyring_RoundtripsSecrets()
    {
        using var tmp = new TempDir();
        var dirs = ViceDirectories.UnifiedAt("vice-test", tmp.Path);
        var k = new FileKeyring(dirs);

        await k.SetAsync("github-token", "ghp_abc123");
        await k.SetAsync("openai-key", "sk-xyz");

        Assert.Equal("ghp_abc123", await k.GetAsync("github-token"));
        Assert.Equal("sk-xyz", await k.GetAsync("openai-key"));
        Assert.Equal(new[] { "github-token", "openai-key" }, await k.ListKeysAsync());
    }

    [Fact]
    public async Task FileKeyring_DeleteRemovesAndReturnsTrue()
    {
        using var tmp = new TempDir();
        var dirs = ViceDirectories.UnifiedAt("vice-test", tmp.Path);
        var k = new FileKeyring(dirs);
        await k.SetAsync("key", "v");

        Assert.True(await k.DeleteAsync("key"));
        Assert.False(await k.DeleteAsync("key"));
        Assert.Null(await k.GetAsync("key"));
    }

    [Fact]
    public async Task FileKeyring_PersistsAcrossInstances()
    {
        using var tmp = new TempDir();
        var dirs = ViceDirectories.UnifiedAt("vice-test", tmp.Path);

        var k1 = new FileKeyring(dirs);
        await k1.SetAsync("x", "1");

        var k2 = new FileKeyring(dirs);
        Assert.Equal("1", await k2.GetAsync("x"));
    }
}
