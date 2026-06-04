using System.Threading.Tasks;
using Vice.Core;
using Vice.Display;
using Vice.Host;
using Xunit;
using static Vice.Core.Dsl;

namespace Vice.Tests;

public class StatusCommandTests
{
    [Fact]
    public async Task Status_NoDaemonRunning_ReportsAndExitsSuccess()
    {
        var c = new RecordingConsole();
        var app = new ViceApp("vice-test-status-" + System.Guid.NewGuid().ToString("N"), "9.9.9",
            description: "test",
            console: c, status: NullStatusDisplay.Instance);

        var exit = await app.RunAsync(new[] { "status" });

        Assert.Equal(0, exit);
        Assert.Contains("No vice-test-status-", c.Output);
        Assert.Contains("daemon running", c.Output);
    }
}
