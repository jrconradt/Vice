using System.Runtime.CompilerServices;
using Xunit;

namespace Vice.Generators.Tests;

internal static class GoldenFile
{
    private const string UPDATE_GOLDENS_ENV_VAR = "UPDATE_GOLDENS";

    public static void Verify(
        string name,
        string actual,
        [CallerFilePath] string callerFile = "")
    {
        var normalized = actual.ReplaceLineEndings("\n");
        var dir = Path.Combine(Path.GetDirectoryName(callerFile)!, "Goldens");
        var path = Path.Combine(dir, name);

        if (!File.Exists(path))
        {
            if (!UpdateRequested())
            {
                Assert.Fail($"Golden '{name}' did not exist at {path}. Set {UPDATE_GOLDENS_ENV_VAR}=1 and re-run to write a baseline, then inspect it before committing.");
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(path, normalized);
            Assert.Fail($"Golden '{name}' did not exist; wrote a new baseline at {path}. Inspect it and re-run.");
        }

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.Equal(expected, normalized);
    }

    private static bool UpdateRequested()
    {
        var value = Environment.GetEnvironmentVariable(UPDATE_GOLDENS_ENV_VAR);
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value != "0"
            && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }
}
