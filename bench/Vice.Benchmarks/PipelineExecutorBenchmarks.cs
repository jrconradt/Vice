using BenchmarkDotNet.Attributes;
using Vice.Core;
using Vice.Display;
using Vice.Execution;
using Vice.Host;
using Vice.Lexicon;
using static Vice.Core.Dsl;

namespace Vice.Benchmarks;

[MemoryDiagnoser]
public class PipelineExecutorBenchmarks
{
    private ViceApp _thenApp = null!;
    private ViceApp _andApp = null!;
    private ViceApp _orApp = null!;

    private static readonly string[] ThenArgs = { "emit", "then", "consume" };

    private static readonly string[] AndArgs = { "first", "and", "second" };

    private static readonly string[] OrArgs = { "try", "or", "fallback" };

    [GlobalSetup]
    public void Setup()
    {
        _thenApp = BuildThenApp();
        _andApp = BuildAndApp();
        _orApp = BuildOrApp();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _thenApp.DisposeAsync().ConfigureAwait(false);
        await _andApp.DisposeAsync().ConfigureAwait(false);
        await _orApp.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark(Baseline = true)]
    public Task<int> ThenPipeline()
    {
        return _thenApp.RunAsync(ThenArgs);
    }

    [Benchmark]
    public Task<int> AndPipeline()
    {
        return _andApp.RunAsync(AndArgs);
    }

    [Benchmark]
    public Task<int> OrPipeline()
    {
        return _orApp.RunAsync(OrArgs);
    }

    private static ViceApp NewApp()
    {
        return new ViceApp(
            "vice",
            "1.0.0",
            description: null,
            console: NullConsoleWriter.Instance,
            status: NullStatusDisplay.Instance);
    }

    private static ViceApp BuildThenApp()
    {
        var app = NewApp();
        app.RegisterPipeline(
            verb("emit") > Connectors.Then() > noun("consume"),
            "then pipeline",
            defaultHandler: (ctx, ct) => Task.FromResult(0),
            stageHandlers: new()
            {
                [0] = (ctx, ct) =>
                {
                    ctx.Console.Write("alpha");
                    return Task.FromResult(0);
                },
                [1] = (ctx, ct) =>
                {
                    GC.KeepAlive(ctx.PipelineInput);
                    return Task.FromResult(0);
                },
            });
        return app;
    }

    private static ViceApp BuildAndApp()
    {
        var app = NewApp();
        app.RegisterPipeline(
            verb("first") > Connectors.AndPipe() > noun("second"),
            "and pipeline",
            defaultHandler: (ctx, ct) => Task.FromResult(0),
            stageHandlers: new()
            {
                [0] = (ctx, ct) =>
                {
                    ctx.Console.Write("A");
                    return Task.FromResult(0);
                },
                [1] = (ctx, ct) =>
                {
                    ctx.Console.Write("B");
                    return Task.FromResult(0);
                },
            });
        return app;
    }

    private static ViceApp BuildOrApp()
    {
        var app = NewApp();
        app.RegisterPipeline(
            verb("try") > Connectors.Or() > noun("fallback"),
            "or pipeline",
            defaultHandler: (ctx, ct) => Task.FromResult(0),
            stageHandlers: new()
            {
                [0] = (ctx, ct) => Task.FromResult(0),
                [1] = (ctx, ct) => Task.FromResult(0),
            });
        return app;
    }
}
