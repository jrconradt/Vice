using System.Reflection;
using BenchmarkDotNet.Running;
using Vice.Benchmarks;

var config = new ViceBenchmarkConfig();
BenchmarkSwitcher
    .FromAssembly(Assembly.GetExecutingAssembly())
    .Run(args, config);
