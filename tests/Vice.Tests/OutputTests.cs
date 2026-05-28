using Vice.Display;
using Xunit;

namespace Vice.Tests;

public class OutputTests
{
    [Fact]
    public void BufferingConsoleWriter_AccumulatesWrites_UntilFlush()
    {
        var inner = new RecordingConsole();
        var buf = new BufferingConsoleWriter(inner);

        buf.Write("a");
        buf.WriteLine("b");
        buf.WriteLine();

        Assert.Equal("", inner.Output);

        buf.Flush();

        Assert.Equal("ab" + Environment.NewLine + Environment.NewLine, inner.Output);
    }

    [Fact]
    public void BufferingConsoleWriter_WriteError_IsBuffered_NotImmediate()
    {
        var inner = new RecordingConsole();
        var buf = new BufferingConsoleWriter(inner);

        buf.WriteError("oops");
        Assert.Empty(inner.Error);

        buf.Flush();
        Assert.Contains("oops", inner.Error);
    }

    [Fact]
    public void CapturingConsoleWriter_RecordsAndForwards()
    {
        var inner = new RecordingConsole();
        var cap = new CapturingConsoleWriter(inner);

        cap.WriteLine("hi");
        cap.Write("there");

        Assert.Equal("hi" + Environment.NewLine + "there", cap.CapturedOutput);
        Assert.Equal("hi" + Environment.NewLine + "there", inner.Output);
    }

    [Fact]
    public void CapturingConsoleWriter_StripsAnsi()
    {
        var inner = new RecordingConsole();
        var cap = new CapturingConsoleWriter(inner);

        cap.Write("[31mred[0m");

        Assert.Equal("red", cap.CapturedOutput);
    }

    [Fact]
    public void CapturingConsoleWriter_Reset_ClearsCapture()
    {
        var inner = new RecordingConsole();
        var cap = new CapturingConsoleWriter(inner);

        cap.WriteLine("x");
        cap.Reset();
        Assert.Equal("", cap.CapturedOutput);
    }

    [Fact]
    public void NullConsoleWriter_Discards()
    {
        var n = NullConsoleWriter.Instance;
        n.Write("anything");
        n.WriteLine("anything");
        n.WriteLine();
        n.WriteError("anything");

    }
}
