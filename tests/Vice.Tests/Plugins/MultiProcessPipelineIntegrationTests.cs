using System.Text;
using Vice.Commands;
using Vice.Logging;
using Vice.Plugins;
using Vice.Tests;
using Xunit;

namespace Vice.Tests.Plugins;

[Collection("EnvVarSerial")]
public class MultiProcessPipelineIntegrationTests
{
    private const string Payload = "the quick brown fox jumps over the lazy dog 0123456789";

    private readonly EnvVarSerialFixture _serial;

    public MultiProcessPipelineIntegrationTests(EnvVarSerialFixture serial)
    {
        _serial = serial;
    }

    private static void RestrictDir(string dir)
    {
        UnixPerms.Set(
            dir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void WriteScript(string path, string body)
    {
        File.WriteAllText(path, body);
        UnixPerms.Set(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [UnixOnlyFact]
    public async Task PumpOne_StreamsBytesFromProducerToConsumer()
    {
        using var pluginDir = new TempDir();
        RestrictDir(pluginDir.Path);

        WriteScript(
            Path.Combine(pluginDir.Path, "vice-prod"),
            $"#!/bin/sh\nprintf '%s' '{Payload}'\n");
        WriteScript(
            Path.Combine(pluginDir.Path, "vice-sink"),
            "#!/bin/sh\ncat > \"$1\"\n");

        var outPath = Path.Combine(pluginDir.Path, "out.txt");

        using var env = new EnvScope(
            _serial,
            ("VICE_PLUGIN_DIR", pluginDir.Path),
            ("XDG_DATA_HOME", null));

        var segments = RawArgsSplitter.Split(new[] { "prod", "then", "sink", outPath });

        var exit = await MultiProcessPipeline.RunAsync(
            "vice",
            segments,
            new CommandRegistry(),
            NullViceLogger.Instance,
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(Payload, await File.ReadAllTextAsync(outPath, Encoding.UTF8));
    }

    [UnixOnlyFact]
    public async Task GlobalLocale_IsPropagatedToDownstreamSegments()
    {
        using var pluginDir = new TempDir();
        RestrictDir(pluginDir.Path);

        var argsPath = Path.Combine(pluginDir.Path, "downstream-args.txt");

        WriteScript(
            Path.Combine(pluginDir.Path, "vice-prod"),
            $"#!/bin/sh\nprintf '%s' '{Payload}'\n");
        WriteScript(
            Path.Combine(pluginDir.Path, "vice-sink"),
            $"#!/bin/sh\nprintf '%s ' \"$@\" > '{argsPath}'\ncat > /dev/null\n");

        using var env = new EnvScope(
            _serial,
            ("VICE_PLUGIN_DIR", pluginDir.Path),
            ("XDG_DATA_HOME", null));

        var segments = RawArgsSplitter.Split(
            new[] { "--locale", "de-DE", "prod", "then", "sink" });

        var exit = await MultiProcessPipeline.RunAsync(
            "vice",
            segments,
            new CommandRegistry(),
            NullViceLogger.Instance,
            CancellationToken.None);

        Assert.Equal(0, exit);

        var recorded = await File.ReadAllTextAsync(argsPath, Encoding.UTF8);
        Assert.Contains("--locale de-DE", recorded);
    }

    [UnixOnlyFact]
    public async Task PumpFan_DeliversIdenticalBytesToEveryDownstream()
    {
        using var pluginDir = new TempDir();
        RestrictDir(pluginDir.Path);

        WriteScript(
            Path.Combine(pluginDir.Path, "vice-prod"),
            $"#!/bin/sh\nprintf '%s' '{Payload}'\n");
        WriteScript(
            Path.Combine(pluginDir.Path, "vice-sink"),
            "#!/bin/sh\ncat > \"$1\"\n");

        var outA = Path.Combine(pluginDir.Path, "out-a.txt");
        var outB = Path.Combine(pluginDir.Path, "out-b.txt");

        using var env = new EnvScope(
            _serial,
            ("VICE_PLUGIN_DIR", pluginDir.Path),
            ("XDG_DATA_HOME", null));

        var segments = RawArgsSplitter.Split(
            new[] { "prod", "then", "sink", outA, "fan", "sink", outB });

        var exit = await MultiProcessPipeline.RunAsync(
            "vice",
            segments,
            new CommandRegistry(),
            NullViceLogger.Instance,
            CancellationToken.None);

        Assert.Equal(0, exit);

        var bytesA = await File.ReadAllTextAsync(outA, Encoding.UTF8);
        var bytesB = await File.ReadAllTextAsync(outB, Encoding.UTF8);
        Assert.Equal(Payload, bytesA);
        Assert.Equal(Payload, bytesB);
    }
}
