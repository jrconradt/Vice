using System.Threading.Tasks;
using Vice.Commands;
using Vice.Core;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Foundation.Execution;
using Vice.Jobs;
using Vice.Logging;
using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class SadPath_SessionBuiltinsTests
{
    private static (CommandExecutor Exec, RecordingConsole Con) Build()
    {
        var registry = new CommandRegistry();
        var console = new RecordingConsole();
        var history = new InputHistory();

        SessionBuiltins.RegisterChains(registry,
                                       Array.Empty<IJobRunner>(),
                                       NullViceLogger.Instance);
        var builtins = new SessionBuiltinRegistry(history);
        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance,
            builtins: builtins);

        return (executor, console);
    }

    [Fact]
    public async Task JobRun_InvalidDescriptor_ReturnsUsageError()
    {
        var (exec, console) = Build();

        var exit = await exec.ExecuteAsync(new[] { "job", "run", "not-json" });

        Assert.Equal(ViceExitCode.USAGE_ERROR, exit);
        Assert.Equal("", console.Output);
    }

    [Fact]
    public async Task JobRun_UnknownKind_ReturnsFailure()
    {
        var (exec, _) = Build();

        var exit = await exec.ExecuteAsync(new[] { "job", "run", "\"{\"Kind\":\"Ghost\",\"Label\":\"x\",\"Options\":{}}\"" });

        Assert.Equal(ViceExitCode.FAILURE, exit);
    }
}
