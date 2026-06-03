using System.IO.Compression;
using System.Text;
using Vice.Foundation.Execution;
using Xunit;

namespace Vice.Cli.Tests;

public class FileCommandsEndToEndTests
{
    private static readonly IReadOnlyDictionary<string, string> QuietEnv = new Dictionary<string, string>
    {
        ["VICE_LOG_LEVEL"] = "error",
    };

    private static string MakeScratchDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vice-fileio-e2e-{Guid.NewGuid():N}");
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
    public async Task Read_ExistingFile_DecodesContentToStdout()
    {
        var scratch = MakeScratchDir();
        try
        {
            var file = Path.Combine(scratch, "hello.txt");
            await File.WriteAllTextAsync(file, "hello-from-read");

            var result = await CliProcess.RunAsync(
                CliProcess.ViceCliDll,
                new[] { "read", file },
                environment: QuietEnv);

            Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
            Assert.Contains("hello-from-read", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task Read_MissingFile_ExitsFailure()
    {
        var scratch = MakeScratchDir();
        try
        {
            var missing = Path.Combine(scratch, "does-not-exist.txt");

            var result = await CliProcess.RunAsync(
                CliProcess.ViceCliDll,
                new[] { "read", missing },
                environment: QuietEnv);

            Assert.NotEqual(ViceExitCode.SUCCESS, result.ExitCode);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task SearchFiles_ByNameGlob_PrintsMatchingPath()
    {
        var scratch = MakeScratchDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(scratch, "match.log"), "x");
            await File.WriteAllTextAsync(Path.Combine(scratch, "skip.txt"), "y");

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
                },
                environment: QuietEnv);

            Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
            Assert.Contains("match.log", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("skip.txt", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task SearchFiles_HiddenFileExcludedByDefault_IncludedWithFlag()
    {
        var scratch = MakeScratchDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(scratch, ".secret.cfg"), "x");

            var without = await CliProcess.RunAsync(
                CliProcess.ViceCliDll,
                new[] { "search", "files", "by", "name", "*.cfg", "in", scratch },
                environment: QuietEnv);

            Assert.Equal(ViceExitCode.SUCCESS, without.ExitCode);
            Assert.DoesNotContain(".secret.cfg", without.StdOut, StringComparison.Ordinal);

            var with = await CliProcess.RunAsync(
                CliProcess.ViceCliDll,
                new[] { "--include-hidden", "search", "files", "by", "name", "*.cfg", "in", scratch },
                environment: QuietEnv);

            Assert.Equal(ViceExitCode.SUCCESS, with.ExitCode);
            Assert.Contains(".secret.cfg", with.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task SearchFolders_ByNameGlob_PrintsMatchingDirectory()
    {
        var scratch = MakeScratchDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(scratch, "target-dir"));
            Directory.CreateDirectory(Path.Combine(scratch, "other"));

            var result = await CliProcess.RunAsync(
                CliProcess.ViceCliDll,
                new[] { "search", "folders", "by", "name", "target-*", "in", scratch },
                environment: QuietEnv);

            Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
            Assert.Contains("target-dir", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("other", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task SearchFiles_InvalidRegex_ExitsUsageError()
    {
        var scratch = MakeScratchDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(scratch, "a.txt"), "x");

            var result = await CliProcess.RunAsync(
                CliProcess.ViceCliDll,
                new[] { "--regex", "search", "files", "by", "name", "(unclosed", "in", scratch },
                environment: QuietEnv);

            Assert.Equal(ViceExitCode.USAGE_ERROR, result.ExitCode);
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task Unarchive_ZipToDestination_ExtractsEntries()
    {
        var scratch = MakeScratchDir();
        try
        {
            var zipPath = Path.Combine(scratch, "bundle.zip");
            using (var fs = File.Create(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                var entry = archive.CreateEntry("note.txt", CompressionLevel.NoCompression);
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes("extracted-payload");
                s.Write(bytes, 0, bytes.Length);
            }

            var dest = Path.Combine(scratch, "out");

            var result = await CliProcess.RunAsync(
                CliProcess.ViceCliDll,
                new[] { "unarchive", zipPath, "to", "dir", dest },
                environment: QuietEnv);

            Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(dest, "note.txt")));
            Assert.Equal("extracted-payload", await File.ReadAllTextAsync(Path.Combine(dest, "note.txt")));
        }
        finally
        {
            Cleanup(scratch);
        }
    }

    [Fact]
    public async Task Unarchive_NonArchive_ExitsUsageError()
    {
        var scratch = MakeScratchDir();
        try
        {
            var notArchive = Path.Combine(scratch, "plain.txt");
            await File.WriteAllTextAsync(notArchive, "not an archive");

            var result = await CliProcess.RunAsync(
                CliProcess.ViceCliDll,
                new[] { "unarchive", notArchive },
                environment: QuietEnv);

            Assert.Equal(ViceExitCode.USAGE_ERROR, result.ExitCode);
        }
        finally
        {
            Cleanup(scratch);
        }
    }
}
