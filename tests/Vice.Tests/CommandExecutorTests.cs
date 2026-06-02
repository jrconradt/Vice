using System.Threading.Tasks;
using Vice;
using Vice.Commands;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Logging;
using Vice.Session;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class CommandExecutorTests
{
    private static CommandExecutor BuildExecutor(out CommandRegistry registry, out RecordingConsole console)
    {
        registry = new CommandRegistry();
        console = new RecordingConsole();
        return new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance, session: null);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesAndInvokesHandler()
    {
        var executor = BuildExecutor(out var registry, out _);
        var called = false;
        registry.Register(verb("ping"), "ping", (ctx, ct) => { called = true; return Task.FromResult(0); });

        var exit = await executor.ExecuteAsync("ping");

        Assert.Equal(0, exit);
        Assert.True(called);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesQuotedArgs()
    {
        var executor = BuildExecutor(out var registry, out _);
        string? bound = null;
        registry.Register(verb("greet") * target("name"), "greet", (ctx, ct) =>
        {
            bound = ctx["name"];
            return Task.FromResult(0);
        });

        await executor.ExecuteAsync("greet \"Alice Bob\"");
        Assert.Equal("Alice Bob", bound);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesExitCode()
    {
        var executor = BuildExecutor(out var registry, out _);
        registry.Register(verb("die"), "die", (ctx, ct) => Task.FromResult(13));
        Assert.Equal(13, await executor.ExecuteAsync("die"));
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCommand_ReturnsNonZero()
    {
        var executor = BuildExecutor(out _, out var console);
        Assert.NotEqual(0, await executor.ExecuteAsync("nothing-here"));
        Assert.NotEmpty(console.Error);
    }

    [Fact]
    public async Task ExecuteAsync_HelpFlag_PrintsHelp()
    {
        var executor = BuildExecutor(out var registry, out var console);
        registry.Register(verb("hello"), "hello desc", (ctx, ct) => Task.FromResult(0));

        Assert.Equal(0, await executor.ExecuteAsync("--help"));
        Assert.Contains("hello", console.Output);
    }
}
