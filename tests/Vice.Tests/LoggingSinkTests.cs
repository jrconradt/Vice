using System;
using System.IO;
using System.Threading.Tasks;
using Vice;
using Vice.Logging;
using Xunit;

namespace Vice.Tests;

public class LoggingSinkTests
{
    [Theory]
    [InlineData("trace", ViceLogLevel.Trace)]
    [InlineData("TRACE", ViceLogLevel.Trace)]
    [InlineData("debug", ViceLogLevel.Debug)]
    [InlineData("info", ViceLogLevel.Info)]
    [InlineData("warn", ViceLogLevel.Warn)]
    [InlineData("warning", ViceLogLevel.Warn)]
    [InlineData("error", ViceLogLevel.Error)]
    [InlineData("  Error  ", ViceLogLevel.Error)]
    public void Parse_KnownLevel_MapsToLevel(string raw, ViceLogLevel expected)
    {
        Assert.Equal(expected, LogLevelEnv.Parse(raw, "vice"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankOrNull_DefaultsToInfo(string? raw)
    {
        Assert.Equal(ViceLogLevel.Info, LogLevelEnv.Parse(raw, "vice"));
    }

    [Fact]
    public void Parse_InvalidValue_WarnsToStderrAndDefaultsToInfo()
    {
        var priorError = System.Console.Error;
        var captured = new StringWriter();
        System.Console.SetError(captured);
        try
        {
            var result = LogLevelEnv.Parse("loud", "vice");
            Assert.Equal(ViceLogLevel.Info, result);
        }
        finally
        {
            System.Console.SetError(priorError);
        }

        var text = captured.ToString();
        Assert.Contains("unknown VICE_LOG_LEVEL 'loud'", text);
        Assert.Contains("defaulting to info.", text);
    }

    [Fact]
    public async Task ConsoleLogSink_BelowMinLevel_WritesNothing()
    {
        var writer = new StringWriter();
        var sink = new ConsoleLogSink(ViceLogLevel.Warn, writer);

        Assert.False(sink.IsEnabled(ViceLogLevel.Info));
        sink.Log(ViceLogLevel.Info, "suppressed");

        await sink.DisposeAsync();

        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public async Task ConsoleLogSink_AtOrAboveMinLevel_WritesFormattedLine()
    {
        var writer = new StringWriter();
        var sink = new ConsoleLogSink(ViceLogLevel.Info, writer);

        Assert.True(sink.IsEnabled(ViceLogLevel.Warn));
        sink.Log(ViceLogLevel.Warn, "hello");

        await sink.DisposeAsync();

        var text = writer.ToString();
        Assert.Contains("[WARN]", text);
        Assert.Contains("hello", text);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public async Task ConsoleLogSink_WithException_IncludesExceptionDetail()
    {
        var writer = new StringWriter();
        var sink = new ConsoleLogSink(ViceLogLevel.Trace, writer);

        sink.Log(ViceLogLevel.Error, "boom", new InvalidOperationException("bad state"));

        await sink.DisposeAsync();

        var text = writer.ToString();
        Assert.Contains("[ERROR]", text);
        Assert.Contains("boom", text);
        Assert.Contains("InvalidOperationException: bad state", text);
    }
}
