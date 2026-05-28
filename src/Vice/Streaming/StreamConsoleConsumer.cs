using Vice.Streaming;

namespace Vice.Streaming;

internal static class StreamConsoleConsumer
{
    public static async Task<int> HandleAsync(IConsumingCommandContext<byte[]> ctx, CancellationToken ct)
    {
        await using var stdout = System.Console.OpenStandardOutput();
        await StreamLoop.RunAsync(ctx.Input, async chunk =>
        {
            await stdout.WriteAsync(chunk, ct).ConfigureAwait(false);
        }, ct);
        await stdout.FlushAsync(ct);
        return 0;
    }
}
