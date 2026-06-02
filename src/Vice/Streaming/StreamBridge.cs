using Vice.Contracts;

namespace Vice.Streaming;

internal static class StreamBridge
{
    public static async Task<string> DrainToStringAsync<T>(IStreamInput<T> input, CancellationToken ct)
    {
        var result = "";
        await foreach (var item in input.ReadAllAsync(ct))
        {
            result += $"{item?.ToString()}{Environment.NewLine}";
        }
        return result;
    }

    public static async Task PushStringAsStreamAsync(
    string? input, IStreamContext<string> stream, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(input))
        {
            var lines = input.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                await stream.YieldAsync(line.TrimEnd('\r'), ct);
            }
        }
        stream.Complete();
    }

    public static async Task PumpStdinAsync(IStreamContext<byte[]> stream, CancellationToken ct)
    {
        try
        {
            var stdin = System.Console.OpenStandardInput();
            var buffer = new byte[65536];
            int read;
            while ((read = await stdin.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                await stream.YieldAsync(chunk, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            stream.Complete();
        }
    }
}
