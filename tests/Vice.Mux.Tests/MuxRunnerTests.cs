using System.Text;
using Vice.Mux.Commands;
using Vice.Mux.Strategies;
using Xunit;

namespace Vice.Mux.Tests;

public class MuxRunnerTests
{
    private sealed class SinkPaths : IDisposable
    {
        public string Dir { get; }
        public string[] Files { get; }

        public SinkPaths(int count)
        {
            Dir = Path.Combine(Path.GetTempPath(), $"vice-mux-run-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Dir);
            Files = new string[count];
            for (int i = 0; i < count; i++)
            {
                Files[i] = Path.Combine(Dir, $"sink-{i}.bin");
            }
        }

        public string Spec => string.Join(",", Files.Select(f => $"file:{f}"));

        public void Dispose()
        {
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }

    [Fact]
    public async Task Route_RoundRobin_PartitionsChunksAcrossSinks()
    {
        using var paths = new SinkPaths(3);
        var input = new MemoryStream(Encoding.ASCII.GetBytes("AAAABBBBCCCC"));
        var targets = new Dictionary<string, string>
        {
            ["strategy"] = "roundrobin",
            ["sinks"] = paths.Spec,
        };
        var globals = new Dictionary<string, string?>
        {
            ["chunk-size"] = "4",
        };
        var ctx = MuxCommandContextFactory.Build(targets, globals);

        var code = await MuxRunner.RunAsync(
            ctx,
            input,
            CancellationToken.None,
            StrategyRegistry.Default(),
            requireN: false);

        Assert.Equal(0, code);
        Assert.Equal("AAAA", await File.ReadAllTextAsync(paths.Files[0]));
        Assert.Equal("BBBB", await File.ReadAllTextAsync(paths.Files[1]));
        Assert.Equal("CCCC", await File.ReadAllTextAsync(paths.Files[2]));
    }

    [Fact]
    public async Task Broadcast_WritesIdenticalBytesToEverySink()
    {
        using var paths = new SinkPaths(3);
        var payload = Encoding.ASCII.GetBytes("the-same-everywhere");
        var input = new MemoryStream(payload);
        var targets = new Dictionary<string, string>
        {
            ["strategy"] = "broadcast",
            ["sinks"] = paths.Spec,
        };
        var globals = new Dictionary<string, string?>
        {
            ["chunk-size"] = "8",
        };
        var ctx = MuxCommandContextFactory.Build(targets, globals);

        var code = await MuxRunner.RunAsync(
            ctx,
            input,
            CancellationToken.None,
            StrategyRegistry.Default(),
            requireN: false);

        Assert.Equal(0, code);
        foreach (var file in paths.Files)
        {
            Assert.Equal(payload, await File.ReadAllBytesAsync(file));
        }
    }

    [Fact]
    public async Task Split_FillsAllSinksAndReconcilesN()
    {
        using var paths = new SinkPaths(2);
        var input = new MemoryStream(Encoding.ASCII.GetBytes("XXYY"));
        var targets = new Dictionary<string, string>
        {
            ["strategy"] = "roundrobin",
            ["sinks"] = paths.Spec,
            ["n"] = "2",
        };
        var globals = new Dictionary<string, string?>
        {
            ["chunk-size"] = "2",
        };
        var ctx = MuxCommandContextFactory.Build(targets, globals);

        var code = await MuxRunner.RunAsync(
            ctx,
            input,
            CancellationToken.None,
            StrategyRegistry.Default(),
            requireN: true);

        Assert.Equal(0, code);
        Assert.Equal("XX", await File.ReadAllTextAsync(paths.Files[0]));
        Assert.Equal("YY", await File.ReadAllTextAsync(paths.Files[1]));
    }

    [Fact]
    public async Task Split_RejectsSinkCountMismatch()
    {
        using var paths = new SinkPaths(2);
        var input = new MemoryStream(Encoding.ASCII.GetBytes("data"));
        var targets = new Dictionary<string, string>
        {
            ["strategy"] = "roundrobin",
            ["sinks"] = paths.Spec,
            ["n"] = "3",
        };
        var ctx = MuxCommandContextFactory.Build(targets);

        await Assert.ThrowsAsync<ArgumentException>(() => MuxRunner.RunAsync(
            ctx,
            input,
            CancellationToken.None,
            StrategyRegistry.Default(),
            requireN: true));
    }

    [Fact]
    public async Task Route_OutOfRangeIndex_Throws()
    {
        using var paths = new SinkPaths(2);
        var registry = StrategyRegistry.Create();
        registry.Register(
            "out-of-range",
            "test strategy that returns an invalid sink index",
            StrategyKind.Unicast,
            route: (chunk, n, s) => 99);
        registry.Freeze();

        var input = new MemoryStream(Encoding.ASCII.GetBytes("payload"));
        var targets = new Dictionary<string, string>
        {
            ["strategy"] = "out-of-range",
            ["sinks"] = paths.Spec,
        };
        var globals = new Dictionary<string, string?>
        {
            ["chunk-size"] = "2",
        };
        var ctx = MuxCommandContextFactory.Build(targets, globals);

        await Assert.ThrowsAsync<InvalidOperationException>(() => MuxRunner.RunAsync(
            ctx,
            input,
            CancellationToken.None,
            registry,
            requireN: false));
    }

    [Fact]
    public async Task UnknownStrategy_Throws()
    {
        using var paths = new SinkPaths(1);
        var input = new MemoryStream(Encoding.ASCII.GetBytes("payload"));
        var targets = new Dictionary<string, string>
        {
            ["strategy"] = "does-not-exist",
            ["sinks"] = paths.Spec,
        };
        var ctx = MuxCommandContextFactory.Build(targets);

        await Assert.ThrowsAsync<ArgumentException>(() => MuxRunner.RunAsync(
            ctx,
            input,
            CancellationToken.None,
            StrategyRegistry.Default(),
            requireN: false));
    }
}
