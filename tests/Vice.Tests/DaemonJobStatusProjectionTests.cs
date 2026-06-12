using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vice.Contracts;
using Vice.Display;
using Vice.Host;
using Vice.Host.Core;
using Vice.Ipc;
using Vice.Jobs;
using Vice.Logging;
using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class DaemonJobStatusProjectionTests
{
    [Fact]
    public async Task JobStatusRequest_ProjectsLedgerRecords_IntoJobStatusEntries()
    {
        var appName = "vice-test-daemon-" + Guid.NewGuid().ToString("N");
        var root = JobLedger.RootFor(appName);

        var app = new ViceApp(appName,
                              "1.0.0",
                              description: null,
                              console: new RecordingConsole(),
                              status: NullStatusDisplay.Instance);

        var state = new SessionState(appName, $"{appName}-pipe");
        var sessionCtx = new SessionContext(new JobSpawner(appName, NullViceLogger.Instance, executablePath: "/bin/false"),
                                            state,
                                            null,
                                            NullViceLogger.Instance,
                                            isInteractive: false);

        var handler = new DaemonMessageHandler(
            app,
            sessionCtx,
            NullConsoleWriter.Instance,
            DaemonMessageHandler.DaemonControlVerbs);

        try
        {
            using var self = Process.GetCurrentProcess();
            var record = new JobState
            {
                Id = Environment.ProcessId,
                Kind = JobKind.Custom("Download"),
                Label = "acme-source",
                Status = JobStatus.Running,
                ProgressCurrent = 50,
                ProgressTotal = 100,
                ProcessStartTimeUtc = self.StartTime.ToUniversalTime(),
            };
            await JobLedger.WriteAsync(root, record, CancellationToken.None);

            var response = await handler.HandleAsync(new JobStatusRequest(), CancellationToken.None);
            var jr = Assert.IsType<JobStatusResponse>(response);

            var entry = Assert.Single(jr.Jobs);
            Assert.Equal(Environment.ProcessId, entry.Id);
            Assert.Equal("Download", entry.Kind);
            Assert.Equal("acme-source", entry.Label);
            Assert.Equal("Running", entry.Status);
            Assert.Equal(0.5, entry.Progress);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task JobStatusRequest_DeadPidRunningRecord_ProjectsAsFailed()
    {
        var appName = "vice-test-daemon-" + Guid.NewGuid().ToString("N");
        var root = JobLedger.RootFor(appName);

        var app = new ViceApp(appName,
                              "1.0.0",
                              description: null,
                              console: new RecordingConsole(),
                              status: NullStatusDisplay.Instance);

        var state = new SessionState(appName, $"{appName}-pipe");
        var sessionCtx = new SessionContext(new JobSpawner(appName, NullViceLogger.Instance, executablePath: "/bin/false"),
                                            state,
                                            null,
                                            NullViceLogger.Instance,
                                            isInteractive: false);

        var handler = new DaemonMessageHandler(
            app,
            sessionCtx,
            NullConsoleWriter.Instance,
            DaemonMessageHandler.DaemonControlVerbs);

        try
        {
            var record = new JobState
            {
                Id = int.MaxValue - 17,
                Kind = JobKind.Custom("Download"),
                Label = "acme-source",
                Status = JobStatus.Running,
                ProcessStartTimeUtc = DateTime.UtcNow,
            };
            await JobLedger.WriteAsync(root, record, CancellationToken.None);

            var response = await handler.HandleAsync(new JobStatusRequest(), CancellationToken.None);
            var jr = Assert.IsType<JobStatusResponse>(response);

            var entry = Assert.Single(jr.Jobs);
            Assert.Equal("Failed", entry.Status);
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
