using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace Vice.Benchmarks;

internal sealed class ViceBenchmarkConfig : ManualConfig
{
    public ViceBenchmarkConfig()
    {
        AddJob(Job.MediumRun
            .WithWarmupCount(3)
            .WithIterationCount(8)
            .WithId("vice"));

        AddExporter(JsonExporter.Full);
        AddExporter(MarkdownExporter.GitHub);
        AddLogger(ConsoleLogger.Default);

        var artifactsPath = Environment.GetEnvironmentVariable("VICE_BENCH_ARTIFACTS");
        if (!string.IsNullOrWhiteSpace(artifactsPath))
        {
            WithArtifactsPath(artifactsPath);
        }

        WithOption(ConfigOptions.JoinSummary, true);
    }
}
