using System.Runtime.CompilerServices;
using Vice.Contracts;
using Vice.Streaming;
using Xunit;

namespace Vice.Tests;

public class StreamBridgeBoundTests
{
    private const int MAX_DRAINED_CHARS = 16 * 1024 * 1024;

    private sealed class ScriptedStreamInput : IStreamInput<string>
    {
        private readonly IReadOnlyList<string> _items;

        public ScriptedStreamInput(IReadOnlyList<string> items)
        {
            _items = items;
        }

        public bool DrivenToCompletion { get; private set; }

        public int ItemsYielded { get; private set; }

        public async IAsyncEnumerable<string> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var item in _items)
            {
                ItemsYielded++;
                yield return item;
                await Task.Yield();
            }
            DrivenToCompletion = true;
        }

        public IAsyncEnumerable<IReadOnlyList<string>> ReadBatchesAsync(int batchSize, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<IReadOnlyList<string>> ReadBatchesAsync(
            int batchSize,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public bool TryRead(out string item)
        {
            throw new NotSupportedException();
        }
    }

    private static (ScriptedStreamInput Input, int BelowCapLen, char BoundaryFill) MakeStraddlingInput()
    {
        var newlineLen = Environment.NewLine.Length;
        var belowCapPayloadLen = MAX_DRAINED_CHARS - newlineLen - 64;
        var belowCapPayload = new string('a', belowCapPayloadLen);
        var boundaryPayload = new string('b', MAX_DRAINED_CHARS);
        var tailPayload = new string('c', MAX_DRAINED_CHARS);
        var input = new ScriptedStreamInput(new[] { belowCapPayload, boundaryPayload, tailPayload });
        return (input, belowCapPayloadLen + newlineLen, 'b');
    }

    [Fact]
    public async Task DrainToStringAsync_ClampsLengthToCap_WhenItemsStraddleCap()
    {
        var (input, _, _) = MakeStraddlingInput();

        var result = await StreamBridge.DrainToStringAsync(input, CancellationToken.None);

        Assert.Equal(MAX_DRAINED_CHARS, result.Length);
    }

    [Fact]
    public async Task DrainToStringAsync_TruncatesBoundarySegment_RatherThanDroppingIt()
    {
        var (input, belowCapLen, boundaryFill) = MakeStraddlingInput();

        var result = await StreamBridge.DrainToStringAsync(input, CancellationToken.None);

        var boundaryCharsKept = MAX_DRAINED_CHARS - belowCapLen;
        Assert.True(boundaryCharsKept > 0);
        Assert.Equal(boundaryFill, result[belowCapLen]);
        Assert.Equal(boundaryFill, result[^1]);
        Assert.Equal(new string(boundaryFill, boundaryCharsKept), result[belowCapLen..]);
    }

    [Fact]
    public async Task DrainToStringAsync_DrivesInputToCompletion_AfterCapReached()
    {
        var (input, _, _) = MakeStraddlingInput();

        await StreamBridge.DrainToStringAsync(input, CancellationToken.None);

        Assert.True(input.DrivenToCompletion);
        Assert.Equal(3, input.ItemsYielded);
    }
}
