using Vice.Contracts;

namespace Vice.Streaming;

internal static class StreamLoop
{
    public static async Task RunAsync<T>(
        IStreamInput<T> input,
        Func<T, ValueTask> onItem,
        CancellationToken ct)
    {
        while (await input.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (input.TryRead(out var item))
            {
                await onItem(item).ConfigureAwait(false);
            }
        }
    }
}
