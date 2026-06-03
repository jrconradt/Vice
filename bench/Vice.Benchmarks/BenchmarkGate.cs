using System.Globalization;
using System.Text.Json;

namespace Vice.Benchmarks;

internal static class BenchmarkGate
{
    private const string GATE_FLAG = "--gate";
    private const string UPDATE_FLAG = "--update-baseline";
    private const double DEFAULT_TOLERANCE = 0.05;
    private const double MAX_TOLERANCE = 0.50;
    private const string TOLERANCE_VAR = "VICE_BENCH_TOLERANCE";

    public static bool WantsGate(string[] args)
    {
        return Array.IndexOf(args, GATE_FLAG) >= 0
            || Array.IndexOf(args, UPDATE_FLAG) >= 0;
    }

    public static string[] StripGateArgs(string[] args)
    {
        return args
            .Where(static a => a != GATE_FLAG && a != UPDATE_FLAG)
            .ToArray();
    }

    public static string ArtifactsPath()
    {
        var configured = Environment.GetEnvironmentVariable("VICE_BENCH_ARTIFACTS");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts");
    }

    public static string BaselinePath()
    {
        var configured = Environment.GetEnvironmentVariable("VICE_BENCH_BASELINE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var here = AppContext.BaseDirectory;
        var project = Path.GetFullPath(Path.Combine(here,
                                                     "..",
                                                     "..",
                                                     ".."));
        return Path.Combine(project, "baseline.json");
    }

    public static int Run(string[] args)
    {
        var update = Array.IndexOf(args, UPDATE_FLAG) >= 0;
        var current = ReadCurrentMeans(ArtifactsPath());
        if (current.Count == 0)
        {
            Console.Error.WriteLine("vice-bench-gate: no BenchmarkDotNet results found to evaluate.");
            return 2;
        }

        var baselinePath = BaselinePath();
        if (update || !File.Exists(baselinePath))
        {
            WriteBaseline(baselinePath, current);
            Console.Out.WriteLine($"vice-bench-gate: wrote baseline with {current.Count} entries to {baselinePath}.");
            return 0;
        }

        var baseline = ReadBaseline(baselinePath);
        var tolerance = ReadTolerance();
        return Evaluate(baseline, current, tolerance);
    }

    private static int Evaluate(IReadOnlyDictionary<string, double> baseline,
                                IReadOnlyDictionary<string, double> current,
                                double tolerance)
    {
        var regressed = 0;
        foreach (var entry in current)
        {
            if (!baseline.TryGetValue(entry.Key, out var baselineMean))
            {
                Console.Out.WriteLine($"vice-bench-gate: new benchmark {entry.Key} ({entry.Value:F1} ns) absent from baseline.");
                continue;
            }

            if (baselineMean <= 0)
            {
                continue;
            }

            var ratio = entry.Value / baselineMean;
            var delta = ratio - 1.0;
            if (delta > tolerance)
            {
                regressed++;
                Console.Error.WriteLine($"vice-bench-gate: REGRESSION {entry.Key}: {entry.Value:F1} ns vs baseline {baselineMean:F1} ns (+{delta:P1}, tolerance {tolerance:P1}).");
            }
            else
            {
                Console.Out.WriteLine($"vice-bench-gate: OK {entry.Key}: {entry.Value:F1} ns vs baseline {baselineMean:F1} ns ({delta:P1}).");
            }
        }

        if (regressed > 0)
        {
            Console.Error.WriteLine($"vice-bench-gate: {regressed} benchmark(s) regressed beyond {tolerance:P1}.");
            return 1;
        }

        Console.Out.WriteLine($"vice-bench-gate: no regressions beyond {tolerance:P1}.");
        return 0;
    }

    private static double ReadTolerance()
    {
        var raw = Environment.GetEnvironmentVariable(TOLERANCE_VAR);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DEFAULT_TOLERANCE;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0
            && parsed <= MAX_TOLERANCE)
        {
            return parsed;
        }

        return DEFAULT_TOLERANCE;
    }

    private static IReadOnlyDictionary<string, double> ReadCurrentMeans(string artifactsPath)
    {
        var means = new Dictionary<string, double>(StringComparer.Ordinal);
        var resultsDir = Path.Combine(artifactsPath, "results");
        if (!Directory.Exists(resultsDir))
        {
            return means;
        }

        foreach (var file in Directory.EnumerateFiles(resultsDir, "*-report-full.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (!document.RootElement.TryGetProperty("Benchmarks", out var benchmarks))
            {
                continue;
            }

            foreach (var benchmark in benchmarks.EnumerateArray())
            {
                if (!benchmark.TryGetProperty("FullName", out var nameElement))
                {
                    continue;
                }

                if (!benchmark.TryGetProperty("Statistics", out var statistics))
                {
                    continue;
                }

                if (statistics.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!statistics.TryGetProperty("Mean", out var meanElement))
                {
                    continue;
                }

                var name = nameElement.GetString();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                means[name] = meanElement.GetDouble();
            }
        }

        return means;
    }

    private static IReadOnlyDictionary<string, double> ReadBaseline(string baselinePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var baseline = new Dictionary<string, double>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("benchmarks", out var benchmarks))
        {
            foreach (var benchmark in benchmarks.EnumerateArray())
            {
                if (!benchmark.TryGetProperty("fullName", out var nameElement))
                {
                    continue;
                }

                if (!benchmark.TryGetProperty("meanNs", out var meanElement))
                {
                    continue;
                }

                var name = nameElement.GetString();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                baseline[name] = meanElement.GetDouble();
            }
        }

        return baseline;
    }

    private static void WriteBaseline(string baselinePath, IReadOnlyDictionary<string, double> means)
    {
        var entries = means
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new BaselineEntry(pair.Key, pair.Value))
            .ToArray();
        var snapshot = new BaselineSnapshot(entries);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        File.WriteAllText(baselinePath, JsonSerializer.Serialize(snapshot, options));
    }

    private sealed record BaselineSnapshot(BaselineEntry[] benchmarks);

    private sealed record BaselineEntry(string fullName, double meanNs);
}
