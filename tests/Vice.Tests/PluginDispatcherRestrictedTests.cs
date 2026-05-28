using System.Threading.Tasks;
using Vice.Commands;
using Vice.Plugins;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

[Collection("EnvVarSerial")]
public class PluginDispatcherRestrictedTests
{
    private static CommandRegistry Registry()
    {
        var r = new CommandRegistry();
        r.Register(verb("list"), "list things", (ctx, ct) => Task.FromResult(0));
        return r;
    }

    [Fact]
    public void PathAlone_NoPluginDir_NotDiscovered()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var pathDir = new TempDir();
        using var fakeXdg = new TempDir();

        var pluginPath = Path.Combine(pathDir.Path, "vice-foo");
        File.WriteAllText(pluginPath, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(pluginPath,
            UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);

        using var env = new EnvScope(
            ("PATH", pathDir.Path + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "")),
            ("VICE_PLUGIN_DIR", null),
            ("XDG_DATA_HOME", fakeXdg.Path));

        var found = PluginDispatcher.TryFind("vice", new[] { "foo" }, Registry(), out _, out _);

        Assert.False(found);
    }

    [Fact]
    public void VicePluginDir_Set_Discovered()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var pluginPath = Path.Combine(dir.Path, "vice-foo");
        File.WriteAllText(pluginPath, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(pluginPath,
            UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);

        using var env = new EnvScope(
            ("VICE_PLUGIN_DIR", dir.Path),
            ("XDG_DATA_HOME", null));

        var found = PluginDispatcher.TryFind("vice", new[] { "foo" }, Registry(),
            out var resolved, out var pluginArgs);

        Assert.True(found);
        Assert.Equal(pluginPath, resolved);
        Assert.Empty(pluginArgs);
    }

    [Fact]
    public void XdgDataHome_FallbackDiscoversPlugin()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var xdg = new TempDir();
        var pluginsDir = Path.Combine(xdg.Path, "vice", "plugins");
        Directory.CreateDirectory(pluginsDir);
        var pluginPath = Path.Combine(pluginsDir, "vice-foo");
        File.WriteAllText(pluginPath, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(pluginPath,
            UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);

        using var env = new EnvScope(
            ("VICE_PLUGIN_DIR", null),
            ("XDG_DATA_HOME", xdg.Path));

        var found = PluginDispatcher.TryFind("vice", new[] { "foo" }, Registry(),
            out var resolved, out _);

        Assert.True(found);
        Assert.Equal(pluginPath, resolved);
    }

    [Fact]
    public void WorldWritablePlugin_Rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var pluginPath = Path.Combine(dir.Path, "vice-foo");
        File.WriteAllText(pluginPath, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(pluginPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);

        using var env = new EnvScope(
            ("VICE_PLUGIN_DIR", dir.Path),
            ("XDG_DATA_HOME", null));

        var found = PluginDispatcher.TryFind("vice", new[] { "foo" }, Registry(), out _, out _);

        Assert.False(found);
    }

    [Fact]
    public void GroupWritablePlugin_Rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var pluginPath = Path.Combine(dir.Path, "vice-foo");
        File.WriteAllText(pluginPath, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(pluginPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute);

        using var env = new EnvScope(
            ("VICE_PLUGIN_DIR", dir.Path),
            ("XDG_DATA_HOME", null));

        var found = PluginDispatcher.TryFind("vice", new[] { "foo" }, Registry(), out _, out _);

        Assert.False(found);
    }

    [Fact]
    public void SymlinkOutOfPluginDir_Rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var pluginsDir = new TempDir();
        using var elsewhere = new TempDir();

        var realTarget = Path.Combine(elsewhere.Path, "real-vice-foo");
        File.WriteAllText(realTarget, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(realTarget,
            UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);

        var link = Path.Combine(pluginsDir.Path, "vice-foo");
        File.CreateSymbolicLink(link, realTarget);

        using var env = new EnvScope(
            ("VICE_PLUGIN_DIR", pluginsDir.Path),
            ("XDG_DATA_HOME", null));

        var found = PluginDispatcher.TryFind("vice", new[] { "foo" }, Registry(), out _, out _);

        Assert.False(found);
    }
}
