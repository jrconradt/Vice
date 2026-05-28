using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class SadPath_InputHistoryTests
{
    private const int MAX_ENTRIES = 1000;

    [Fact]
    public async Task Append_BeyondCap_TrimsOldestEntries()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "history");
        var h = new InputHistory(path);

        for (int i = 0; i < MAX_ENTRIES + 50; i++)
        {
            await h.AppendAsync("cmd-" + i, default);
        }

        Assert.Equal(MAX_ENTRIES, h.GetHistory().Count);

        var h2 = new InputHistory(path);
        h2.Load();
        Assert.Equal(MAX_ENTRIES, h2.GetHistory().Count);
        Assert.Equal("cmd-50", h2.GetHistory()[0]);
        Assert.Equal("cmd-" + (MAX_ENTRIES + 49), h2.GetHistory()[^1]);
    }

    [Fact]
    public void Load_NonexistentPath_ParentDirMissing_DoesNotThrow()
    {
        var deep = Path.Combine(Path.GetTempPath(),
            "vice-tests-nodir-" + Guid.NewGuid().ToString("N"),
            "nested", "history");

        var h = new InputHistory(deep);
        h.Load();
        Assert.Empty(h.GetHistory());
    }
}
