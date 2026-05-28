using System;
using System.IO;
using Vice.Configuration;
using Vice.Logging;
using Xunit;

namespace Vice.Tests;

public class TelemetryTests
{
    static TelemetryTests()
        => Environment.SetEnvironmentVariable(FileTelemetrySink.ConsentEnvVar, "1");

    [Fact]
    public void NullTelemetry_NeverThrows()
    {
        var t = NullTelemetrySink.Instance;
        t.Track("event");
        t.Track("event", new Dictionary<string, string> { ["k"] = "v" });
        t.TrackException(new InvalidOperationException("boom"));
    }

    [Fact]
    public async Task FileTelemetry_AppendsEvent()
    {
        using var tmp = new TempDir();
        var dirs = ViceDirectories.UnifiedAt("vice-test", tmp.Path);
        var t = new FileTelemetrySink(dirs);

        t.Track("test-event", new Dictionary<string, string> { ["foo"] = "bar" });
        await t.FlushAsync();

        var path = Path.Combine(tmp.Path, "telemetry.jsonl");
        Assert.True(File.Exists(path));
        var line = File.ReadAllText(path).TrimEnd('\n');
        Assert.Contains("\"event\":\"test-event\"", line);
        Assert.Contains("\"foo\":\"bar\"", line);
        Assert.Contains("\"timestamp\":", line);
    }

    [Fact]
    public async Task FileTelemetry_AppendsException()
    {
        using var tmp = new TempDir();
        var dirs = ViceDirectories.UnifiedAt("vice-test", tmp.Path);
        var t = new FileTelemetrySink(dirs);

        t.TrackException(new InvalidOperationException("nope"));
        await t.FlushAsync();

        var path = Path.Combine(tmp.Path, "telemetry.jsonl");
        var line = File.ReadAllText(path).TrimEnd('\n');
        Assert.Contains("\"event\":\"exception\"", line);
        Assert.Contains("InvalidOperationException", line);
        Assert.Contains("nope", line);
    }

    [Fact]
    public async Task FileTelemetry_MultipleEventsAreSeparateLines()
    {
        using var tmp = new TempDir();
        var dirs = ViceDirectories.UnifiedAt("vice-test", tmp.Path);
        var t = new FileTelemetrySink(dirs);

        t.Track("a");
        t.Track("b");
        t.Track("c");
        await t.FlushAsync();

        var lines = File.ReadAllLines(Path.Combine(tmp.Path, "telemetry.jsonl"));
        Assert.Equal(3, lines.Length);
    }
}
