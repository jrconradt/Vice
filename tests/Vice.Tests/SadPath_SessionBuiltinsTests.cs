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
    private const int UNKNOWN_ID = 999999;

    private static (CommandExecutor Exec, RecordingConsole Con) Build()
    {
        var registry = new CommandRegistry();
        var console = new RecordingConsole();
        var history = new InputHistory();
        var appName = $"vice-test-{Guid.NewGuid():N}";

        SessionBuiltins.RegisterChains(registry,
                                       appName,
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
    public async Task Cancel_UnknownId_ReportsNoSuchJob()
    {
        var (exec, console) = Build();

        var exit = await exec.ExecuteAsync($"cancel {UNKNOWN_ID}");

        Assert.Equal(ViceExitCode.FAILURE, exit);
        Assert.Contains($"No such job: #{UNKNOWN_ID}.", console.Error);
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
    public async Task JobRun_UnknownKind_WritesFailedRecordAndReturnsFailure()
    {
        var registry = new CommandRegistry();
        var console = new RecordingConsole();
        var history = new InputHistory();
        var appName = $"vice-test-{Guid.NewGuid():N}";

        SessionBuiltins.RegisterChains(registry,
                                       appName,
                                       Array.Empty<IJobRunner>(),
                                       NullViceLogger.Instance);
        var executor = new CommandExecutor(
            registry, TestOptions.All, console,
            NullStatusDisplay.Instance, TerminalCapabilities.None, NullOutputSink.Instance,
            builtins: new SessionBuiltinRegistry(history));

        var root = JobLedger.RootFor(appName);
        try
        {
            var exit = await executor.ExecuteAsync(new[] { "job", "run", "\"{\"Kind\":\"Ghost\",\"Label\":\"x\",\"Options\":{}}\"" });

            Assert.Equal(ViceExitCode.FAILURE, exit);
            var record = await JobLedger.ReadAsync(root,
                                                   Environment.ProcessId,
                                                   NullViceLogger.Instance,
                                                   CancellationToken.None);
            Assert.NotNull(record);
            Assert.Equal(JobStatus.Failed, record!.Status);
            Assert.Contains("No runner registered", record.ErrorMessage);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
