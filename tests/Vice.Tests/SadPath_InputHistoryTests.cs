using Vice.Session;
using Xunit;

namespace Vice.Tests;

public class SadPath_InputHistoryTests
{
    private const int MAX_ENTRIES = 1000;

    [Fact]
    public async Task Append_BeyondCap_TrimsOldestEntries()
    {
        var h = new InputHistory();

        for (int i = 0; i < MAX_ENTRIES + 50; i++)
        {
            await h.AppendAsync("cmd-" + i, default);
        }

        Assert.Equal(MAX_ENTRIES, h.GetHistory().Count);
        Assert.Equal("cmd-50", h.GetHistory()[0]);
        Assert.Equal("cmd-" + (MAX_ENTRIES + 49), h.GetHistory()[^1]);
    }
}
