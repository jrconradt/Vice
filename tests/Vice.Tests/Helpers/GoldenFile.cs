using System.Runtime.CompilerServices;
using Xunit;

namespace Vice.Tests;

internal static class GoldenFile
{
    public static void Verify(
        string name,
        string actual,
        [CallerFilePath] string callerFile = "")
    {
        var normalized = actual.ReplaceLineEndings("\n");
        var dir = Path.Combine(Path.GetDirectoryName(callerFile)!, "Goldens");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);

        if (!File.Exists(path))
        {
            File.WriteAllText(path, normalized);
            Assert.Fail($"Golden '{name}' did not exist; wrote a new baseline at {path}. Inspect it and re-run.");
        }

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.Equal(expected, normalized);
    }
}
