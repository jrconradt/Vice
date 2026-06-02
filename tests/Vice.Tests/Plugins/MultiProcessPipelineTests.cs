using Vice.Commands;
using Vice.Logging;
using Vice.Plugins;
using Xunit;

namespace Vice.Tests.Plugins;

public class MultiProcessPipelineTests
{
    [UnixOnlyFact]
    public async Task MultiProcessPipeline_RejectsAllRegistrySegments()
    {
        var segments = RawArgsSplitter.Split(new[] { "nope", "then", "alsonope" });

        var registry = new CommandRegistry();
        var exit = await MultiProcessPipeline.RunAsync(
            "vice",
            segments,
            registry,
            NullViceLogger.Instance,
            CancellationToken.None);

        Assert.Equal(127, exit);
    }
}
