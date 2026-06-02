using System.Reflection;
using BenchmarkDotNet.Running;
using Vice.Benchmarks;

if (BenchmarkGate.WantsGate(args))
{
    Environment.SetEnvironmentVariable("VICE_BENCH_ARTIFACTS", BenchmarkGate.ArtifactsPath());
    var config = new ViceBenchmarkConfig();
    var runArgs = BenchmarkGate.StripGateArgs(args);
    if (runArgs.Length == 0)
    {
        runArgs = new[] { "--filter", "*", "--join" };
    }

    BenchmarkSwitcher
        .FromAssembly(Assembly.GetExecutingAssembly())
        .Run(runArgs, config);
    return BenchmarkGate.Run(args);
}

var defaultConfig = new ViceBenchmarkConfig();
BenchmarkSwitcher
    .FromAssembly(Assembly.GetExecutingAssembly())
    .Run(args, defaultConfig);
return 0;
