using Vice.Contracts;

namespace Vice.Streaming;

internal static class StreamBridge
{
    private const int MAX_DRAINED_CHARS = 16 * 1024 * 1024;

    public static async Task<string> DrainToStringAsync<T>(IStreamInput<T> input, CancellationToken ct)
    {
        var segments = new List<string>();
        var totalChars = 0;
        await foreach (var item in input.ReadAllAsync(ct))
        {
            if (totalChars >= MAX_DRAINED_CHARS)
            {
                continue;
            }

            var segment = $"{item?.ToString()}{Environment.NewLine}";
            if (segment.Length > MAX_DRAINED_CHARS - totalChars)
            {
                segment = segment[..(MAX_DRAINED_CHARS - totalChars)];
            }

            segments.Add(segment);
            totalChars += segment.Length;
        }
        return string.Concat(segments);
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
