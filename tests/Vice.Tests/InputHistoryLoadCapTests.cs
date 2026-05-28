using System.Text;
using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class InputHistoryLoadCapTests
{
    [Fact]
    public void Load_LargeHistoryFile_DoesNotOomAndKeepsTrailingEntries()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "history");

        var lineCount = 0;
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            var line = new string('a', 1023);
            var target = 10L * 1024 * 1024;
            long written = 0;
            while (written < target)
            {
                writer.Write(line);
                writer.Write($"-{lineCount}\n");
                written += 1024 + 8;
                lineCount++;
            }
            var sentinelStart = lineCount;
            for (var i = 0; i < 5; i++)
            {
                writer.Write($"SENTINEL-{sentinelStart + i}\n");
                lineCount++;
            }
        }

        var history = new InputHistory(path);
        history.Load();

        var entries = history.GetHistory();
        Assert.NotEmpty(entries);
        Assert.True(entries.Count <= 1000);
        Assert.Contains(entries, e => e.StartsWith("SENTINEL-", StringComparison.Ordinal));
        Assert.Equal($"SENTINEL-{lineCount - 1}", entries[^1]);
    }
}
