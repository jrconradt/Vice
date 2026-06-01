using System.Text;
using Vice.Contracts;

namespace Vice.Streaming;

internal static class StreamCountConsumer
{
    public static async Task<int> HandleAsync(IConsumingCommandContext<byte[]> ctx, CancellationToken ct)
    {
        long chunks = 0;
        long bytes = 0;
        await StreamLoop.RunAsync(ctx.Input, chunk =>
        {
            chunks++;
            bytes += chunk.Length;
            return ValueTask.CompletedTask;
        }, ct);
        Vice.Output.Line($"chunks={chunks} bytes={bytes}");
        return 0;
    }
}
