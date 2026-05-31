using System.Threading.Tasks;
using Vice;
using Vice.Commands;
using Vice.Display;
using Vice.Plugins;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

[Collection("EnvVarSerial")]
public class PluginDispatcherTests
{
    private static CommandRegistry Registry()
    {
        var r = new CommandRegistry();
        r.Register(verb("list"), "list things", (ctx, ct) => Task.FromResult(0));
        return r;
    }

    [Fact]
    public void EmptyArgs_NoPlugin()
    {
        Assert.False(PluginDispatcher.TryFind("vice", Array.Empty<string>(), Registry(), out _, out _));
    }

    [Fact]
    public void DashDashFlag_NotConsideredPlugin()
    {
        Assert.False(PluginDispatcher.TryFind("vice", new[] { "--help" }, Registry(), out _, out _));
    }

    [Fact]
    public void SingleDashFlag_NotConsideredPlugin()
    {
        Assert.False(PluginDispatcher.TryFind("vice", new[] { "-v" }, Registry(), out _, out _));
    }

    [Fact]
    public void RegisteredVerb_NotShadowedByPlugin()
    {
        using var dir = new TempDir();
        var pluginName = OperatingSystem.IsWindows() ? "vice-list.exe" : "vice-list";
        File.WriteAllText(Path.Combine(dir.Path, pluginName), "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows())
        {
            UnixPerms.Set(dir.Path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            UnixPerms.Set(Path.Combine(dir.Path, pluginName),
                UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);
        }

        using var env = new EnvScope(
            ("VICE_PLUGIN_DIR", dir.Path),
            ("XDG_DATA_HOME", null));

        Assert.False(PluginDispatcher.TryFind("vice", new[] { "list" }, Registry(), out _, out _));
    }

    [UnixOnlyFact]
    public void UnknownVerb_WithPluginDir_Found()
    {
        using var dir = new TempDir();
        UnixPerms.Set(dir.Path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var pluginPath = Path.Combine(dir.Path, "vice-myextra");
        File.WriteAllText(pluginPath, "#!/bin/sh\nexit 42\n");
        UnixPerms.Set(pluginPath,
            UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);

        using var env = new EnvScope(
            ("VICE_PLUGIN_DIR", dir.Path),
            ("XDG_DATA_HOME", null));

        var found = PluginDispatcher.TryFind("vice", new[] { "myextra", "arg1" }, Registry(),
            out var resolvedPath, out var pluginArgs);

        Assert.True(found);
        Assert.Equal(pluginPath, resolvedPath);
        Assert.Equal(new[] { "arg1" }, pluginArgs);
    }

    [UnixOnlyFact]
    public async Task RunAsync_ForwardsExitCode()
    {
        using var dir = new TempDir();
        var pluginPath = Path.Combine(dir.Path, "vice-myextra");
        File.WriteAllText(pluginPath, "#!/bin/sh\nexit 42\n");
        UnixPerms.Set(pluginPath,
            UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);

        var exit = await PluginDispatcher.RunAsync(pluginPath, Array.Empty<string>(), CancellationToken.None);

        Assert.Equal(42, exit);
    }

    [UnixOnlyFact]
    public async Task RunAsync_PassesArguments()
    {
        using var dir = new TempDir();
        var pluginPath = Path.Combine(dir.Path, "vice-echoargs");
        var outFile = Path.Combine(dir.Path, "captured.txt");
        File.WriteAllText(pluginPath, $"#!/bin/sh\necho \"$@\" > {outFile}\nexit 0\n");
        UnixPerms.Set(pluginPath,
            UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);

        var exit = await PluginDispatcher.RunAsync(pluginPath, new[] { "alpha", "beta gamma" }, CancellationToken.None);

        Assert.Equal(0, exit);
        var captured = File.ReadAllText(outFile).Trim();
        Assert.Contains("alpha", captured);
        Assert.Contains("beta gamma", captured);
    }

    [UnixOnlyFact]
    public async Task ViceApp_DispatchesToPlugin_EndToEnd()
    {
        using var dir = new TempDir();
        UnixPerms.Set(dir.Path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var pluginPath = Path.Combine(dir.Path, "vice-greet");
        File.WriteAllText(pluginPath, "#!/bin/sh\nexit 7\n");
        UnixPerms.Set(pluginPath,
            UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);

        using var env = new EnvScope(
            ("VICE_PLUGIN_DIR", dir.Path),
            ("XDG_DATA_HOME", null));

        var c = new RecordingConsole();
        var app = new ViceApp("vice", "9.9.9", description: "test",
            console: c, status: NullStatusDisplay.Instance);

        var exit = await app.RunAsync(new[] { "greet", "world" });

        Assert.Equal(7, exit);
    }
}
