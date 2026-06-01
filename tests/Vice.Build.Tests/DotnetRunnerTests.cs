using Vice.Build.Dotnet;
using Xunit;

namespace Vice.Build.Tests;

public class DotnetRunnerTests : IDisposable
{
    private readonly IOutputSink _prior;
    private readonly CapturingSink _capture = new();

    public DotnetRunnerTests()
    {
        _prior = Vice.Output.Current;
        Vice.Output.Configure(_capture);
    }

    public void Dispose()
    {
        Vice.Output.Configure(_prior);
    }

    [Fact]
    public async Task NonexistentExecutable_ReturnsNotFoundCode()
    {
        var code = await DotnetRunner.RunAsync(
            "vice-nonexistent-executable-7f3a9c",
            verbose: false,
            ct: default,
            "--version");

        Assert.Equal(127, code);
        Assert.Contains(_capture.Errors, e => e.Contains("not found on PATH", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RealDotnetVersion_CapturesOutput_AndReturnsZero()
    {
        var code = await DotnetRunner.RunAsync(
            "dotnet",
            verbose: false,
            ct: default,
            "--version");

        Assert.Equal(0, code);
        Assert.Empty(_capture.Errors);
        Assert.Contains(_capture.Lines, l => l.Length > 0);
    }

    [UnixOnlyFact]
    public async Task Cancellation_KillsRunningProcessPromptly()
    {
        using var cts = new CancellationTokenSource();
        var run = DotnetRunner.RunAsync("/bin/sleep", verbose: false, ct: cts.Token, "30");

        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(run, completed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private sealed class CapturingSink : IOutputSink
    {
        public List<string> Lines { get; } = new();
        public List<string> Errors { get; } = new();

        public void Line(string text)
        {
            Lines.Add(text);
        }

        public void Line()
        {
            Lines.Add(string.Empty);
        }

        public void Write(string text)
        {
            Lines.Add(text);
        }

        public void Error(string text)
        {
            Errors.Add(text);
        }
    }
}
