using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Vice.Foundation.Execution;
using Xunit;

namespace Vice.Cli.Tests;

public class DocumentedCommandsExistTests
{
    private static readonly IReadOnlyDictionary<string, string> QuietEnv = new Dictionary<string, string>
    {
        ["VICE_LOG_LEVEL"] = "error",
    };

    private static readonly Regex InvocationLine = new(
        @"^(?<tool>vice-mux|vice)\s+(?<verb>[a-z][a-z-]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex RootTransition = new(
        "\"root:(?<verb>[a-z][a-z-]*)\"",
        RegexOptions.Compiled);

    [Fact]
    public async Task EveryDocumentedViceVerbExistsInLiveRegistry()
    {
        await AssertDocumentedVerbsExist(
            "vice",
            CliProcess.ViceCliDll);
    }

    [Fact]
    public async Task EveryDocumentedViceMuxVerbExistsInLiveRegistry()
    {
        await AssertDocumentedVerbsExist(
            "vice-mux",
            CliProcess.ViceMuxCliDll);
    }

    private static async Task AssertDocumentedVerbsExist(string tool, string dll)
    {
        var documented = CollectDocumentedVerbs(tool);
        Assert.NotEmpty(documented);

        var live = await CollectLiveVerbs(dll);
        Assert.NotEmpty(live);

        var missing = documented
            .Where(v => !live.Contains(v))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Documentation references {tool} command(s) absent from the live registry: " +
                $"{string.Join(", ", missing)}. Update the docs or the command registration so they agree.");
    }

    private static IReadOnlySet<string> CollectDocumentedVerbs(string tool)
    {
        var verbs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in MarkdownFiles())
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();
                var match = InvocationLine.Match(trimmed);
                if (!match.Success)
                {
                    continue;
                }

                if (!string.Equals(match.Groups["tool"].Value, tool, StringComparison.Ordinal))
                {
                    continue;
                }

                verbs.Add(match.Groups["verb"].Value);
            }
        }

        return verbs;
    }

    private static async Task<IReadOnlySet<string>> CollectLiveVerbs(string dll)
    {
        var result = await CliProcess.RunAsync(
            dll,
            new[] { "completions", "bash" },
            environment: QuietEnv);
        Assert.Equal(ViceExitCode.SUCCESS, result.ExitCode);

        var verbs = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in RootTransition.Matches(result.StdOut))
        {
            verbs.Add(match.Groups["verb"].Value);
        }

        return verbs;
    }

    private static IEnumerable<string> MarkdownFiles()
    {
        var root = RepoRoot();
        yield return Path.Combine(root, "README.md");
        var docs = Path.Combine(root, "docs");
        foreach (var file in Directory.EnumerateFiles(docs, "*.md", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
        {
            yield return file;
        }
    }

    private static string RepoRoot([CallerFilePath] string callerFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", ".."));
}
