using Vice.Streaming;

namespace Vice.Streaming;

internal static class StreamConsoleConsumer
{
    private const int BUFFER_BYTES = 1 << 16;

    public static async Task<int> HandleAsync(IConsumingCommandContext<byte[]> ctx, CancellationToken ct)
    {
        await using var stdout = new BufferedStream(System.Console.OpenStandardOutput(), BUFFER_BYTES);
        await StreamLoop.RunAsync(ctx.Input, async chunk =>
        {
            await stdout.WriteAsync(chunk, ct).ConfigureAwait(false);
        }, ct);
        await stdout.FlushAsync(ct);
        return 0;
    }
}
