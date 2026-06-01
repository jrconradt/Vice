using System.Threading.Tasks;
using Vice;
using Vice.Display;
using Vice.Execution;
using Vice.Logging;
using Xunit;
using static Vice.Dsl;

namespace Vice.Tests;

public class ViceExitCodeTests
{
    [Fact]
    public void Constants_FollowPosixConvention()
    {
        Assert.Equal(0, ViceExitCode.SUCCESS);
        Assert.Equal(1, ViceExitCode.FAILURE);
        Assert.Equal(2, ViceExitCode.USAGE_ERROR);
        Assert.Equal(130, ViceExitCode.INTERRUPTED);
    }

    [Fact]
    public void CommandResult_StaticInstancesMatchConstants()
    {
        Assert.Equal(ViceExitCode.SUCCESS, CommandResult.Success.ExitCode);
        Assert.Equal(ViceExitCode.FAILURE, CommandResult.Failure.ExitCode);
        Assert.Equal(ViceExitCode.USAGE_ERROR, CommandResult.UsageError.ExitCode);
        Assert.Equal(ViceExitCode.INTERRUPTED, CommandResult.Interrupted.ExitCode);
    }

    [Fact]
    public void ViceError_DefaultExitCode_IsFailure()
    {
        ViceError err = new InvalidState("nope");
        Assert.Equal(ViceExitCode.FAILURE, err.ExitCode);
    }

    [Fact]
    public void BadArgument_ExitCode_IsUsageError()
    {
        ViceError err = new BadArgument("bad input");
        Assert.Equal(ViceExitCode.USAGE_ERROR, err.ExitCode);
    }

    [Fact]
    public async Task UnknownCommand_ReturnsUsageError()
    {
        var c = new RecordingConsole();
        var app = new ViceApp("vice", "9.9.9", description: "test app",
            console: c, status: NullStatusDisplay.Instance);
        app.Register(verb("real"), "real verb", (ctx, ct) => Task.FromResult(0));

        var exit = await app.RunAsync(new[] { "help", "not-a-verb" });

        Assert.Equal(ViceExitCode.USAGE_ERROR, exit);
        Assert.Contains("Unknown command", c.Error);
    }
}
