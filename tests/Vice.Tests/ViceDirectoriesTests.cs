using System.IO;
using Vice.Configuration;
using Xunit;

namespace Vice.Tests;

[Collection("EnvVarSerial")]
public class ViceDirectoriesTests
{
    [Fact]
    public void Defaults_FollowXdgSpec()
    {
        using var env = new EnvScope(
            ("VICE_CONFIG_HOME", null), ("VICE_DATA_HOME", null),
            ("VICE_CACHE_HOME", null), ("VICE_STATE_HOME", null),
            ("XDG_CONFIG_HOME", null), ("XDG_DATA_HOME", null),
            ("XDG_CACHE_HOME", null), ("XDG_STATE_HOME", null));

        var dirs = new ViceDirectories("vice", useLegacyFallback: false);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(home, ".config", "vice"), dirs.ConfigDir);
        Assert.Equal(Path.Combine(home, ".cache", "vice"), dirs.CacheDir);
        Assert.Equal(Path.Combine(home, ".local", "state", "vice"), dirs.StateDir);
        Assert.EndsWith("vice", dirs.DataDir);
    }

    [Fact]
    public void XdgEnvVar_OverridesPlatformDefault()
    {
        using var tmp = new TempDir();
        using var env = new EnvScope(("XDG_CONFIG_HOME", tmp.Path), ("VICE_CONFIG_HOME", null));

        var dirs = new ViceDirectories("vice", useLegacyFallback: false);

        Assert.Equal(Path.Combine(tmp.Path, "vice"), dirs.ConfigDir);
    }

    [Fact]
    public void ViceEnvVar_OverridesXdg()
    {
        using var tmp1 = new TempDir();
        using var tmp2 = new TempDir();
        using var env = new EnvScope(
            ("VICE_CONFIG_HOME", tmp1.Path),
            ("XDG_CONFIG_HOME", tmp2.Path));

        var dirs = new ViceDirectories("vice", useLegacyFallback: false);

        Assert.Equal(tmp1.Path, dirs.ConfigDir);
    }

    [Fact]
    public void CtorOverride_OverridesAllEnvVars()
    {
        using var tmp1 = new TempDir();
        using var tmp2 = new TempDir();
        using var env = new EnvScope(
            ("VICE_CONFIG_HOME", tmp2.Path),
            ("XDG_CONFIG_HOME", tmp2.Path));

        var dirs = new ViceDirectories("vice", configHome: tmp1.Path, useLegacyFallback: false);

        Assert.Equal(tmp1.Path, dirs.ConfigDir);
    }

    [Fact]
    public void UnifiedAt_PutsAllKindsUnderOneDir()
    {
        using var tmp = new TempDir();
        var dirs = ViceDirectories.UnifiedAt("vice", tmp.Path);

        Assert.Equal(tmp.Path, dirs.ConfigDir);
        Assert.Equal(tmp.Path, dirs.DataDir);
        Assert.Equal(tmp.Path, dirs.CacheDir);
        Assert.Equal(tmp.Path, dirs.StateDir);
        Assert.Null(dirs.LegacyDir);
    }

    [Fact]
    public void RuntimeDir_NullByDefault()
    {
        using var env = new EnvScope(("VICE_RUNTIME_DIR", null), ("XDG_RUNTIME_DIR", null));

        var dirs = new ViceDirectories("vice", useLegacyFallback: false);

        Assert.Null(dirs.RuntimeDir);
    }

    [Fact]
    public void RuntimeDir_ResolvedFromXdg()
    {
        using var tmp = new TempDir();
        using var env = new EnvScope(("XDG_RUNTIME_DIR", tmp.Path), ("VICE_RUNTIME_DIR", null));

        var dirs = new ViceDirectories("vice", useLegacyFallback: false);

        Assert.Equal(Path.Combine(tmp.Path, "vice"), dirs.RuntimeDir);
    }

    [Fact]
    public void AppName_FlowsIntoEveryDir()
    {
        using var tmp = new TempDir();
        using var env = new EnvScope(("XDG_CONFIG_HOME", tmp.Path));

        var dirs = new ViceDirectories("chain-asm", useLegacyFallback: false);

        Assert.Equal(Path.Combine(tmp.Path, "chain-asm"), dirs.ConfigDir);
    }

    [Fact]
    public void ResolveFileWithLegacy_PrefersModernWhenItExists()
    {
        using var modern = new TempDir();
        using var legacy = new TempDir();
        var dirs = new ViceDirectories("vice", configHome: modern.Path, useLegacyFallback: false);

        var modernFile = Path.Combine(modern.Path, "history");
        File.WriteAllText(modernFile, "modern");

        var resolved = dirs.ResolveFileWithLegacy(modern.Path, "history");

        Assert.Equal(modernFile, resolved);
    }

    [Fact]
    public void ResolveFileWithLegacy_ReturnsModernPathWhenNeitherExists()
    {
        using var tmp = new TempDir();
        var dirs = new ViceDirectories("vice", configHome: tmp.Path, useLegacyFallback: false);

        var resolved = dirs.ResolveFileWithLegacy(tmp.Path, "history");

        Assert.Equal(Path.Combine(tmp.Path, "history"), resolved);
    }

    [Fact]
    public void EmptyAppName_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ViceDirectories(""));
        Assert.Throws<ArgumentException>(() => new ViceDirectories("   "));
    }
}

internal sealed class EnvScope : IDisposable
{
    private readonly List<(string Name, string? Original)> _previous = new();

    public EnvScope(params (string Name, string? Value)[] sets)
    {
        foreach (var (name, value) in sets)
        {
            _previous.Add((name, Environment.GetEnvironmentVariable(name)));
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, original) in _previous)
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }
}

[CollectionDefinition("EnvVarSerial", DisableParallelization = true)]
public class EnvVarSerialCollection { }
