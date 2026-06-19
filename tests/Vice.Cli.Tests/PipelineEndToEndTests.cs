using Vice.Foundation.Execution;
using Xunit;

namespace Vice.Cli.Tests;

public class PipelineEndToEndTests
{
    private static readonly IReadOnlyDictionary<string, string> QuietEnv = new Dictionary<string, string>
    {
        ["VICE_LOG_LEVEL"] = "error",
    };

    private static string MakeScratchDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vice-pipeline-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
    }

    [Fact]
    public async Task SearchFiles_ThenWriteToFile_PipesMatchedContentToDestination()
    {
        var scratch = MakeScratchDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(scratch, "match.log"), "piped-payload-through-the-real-cli");
            await File.WriteAllTextAsync(Path.Combine(scratch, "skip.txt"), "should-not-be-piped");

            var dest = Path.Combine(scratch, "out.bin");

            var result = await CliProcess.RunAsync(
                CliProcess.ViceCliDll,
                new[]
                {
                    "search",
                    "files",
                    "by",
                    "name",
                    "*.log",
                    "in",
                    scratch,
                    "then",
                    "write",
                    "to",
                    "file",
                    dest,
                },
                environment: QuietEnv);

            Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
            Assert.True(File.Exists(dest));

            var written = await File.ReadAllTextAsync(dest);
            Assert.Equal("piped-payload-through-the-real-cli", written);
            Assert.DoesNotContain("should-not-be-piped", written, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(scratch);
        }
    }
}
