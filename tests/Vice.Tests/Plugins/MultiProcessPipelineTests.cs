using Vice.Commands;
using Vice.Plugins;
using Xunit;

namespace Vice.Tests.Plugins;

public class MultiProcessPipelineTests
{
    [Fact]
    public async Task MultiProcessPipeline_RejectsAllRegistrySegments()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var segments = RawArgsSplitter.Split(new[] { "nope", "then", "alsonope" });

        var registry = new CommandRegistry();
        var exit = await MultiProcessPipeline.RunAsync(
            "vice",
            segments,
            registry,
            CancellationToken.None);

        Assert.Equal(127, exit);
    }
}
