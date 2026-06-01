using Vice.Composition;
using Vice.Execution;
using Vice.Lexicon;
using Vice.Mux;

namespace Vice.Mux.Commands;

[ViceCommandPack]
public static class InspectCommands
{
    public static void Register(IViceApp app)
    {
        app.Register(
            Verbs.Inspect(),
            "Passthrough: copies stdin to stdout; emits chunk/byte/format metadata to stderr",
            HandleAsync);
    }

    private static async Task<int> HandleAsync(CommandContext ctx, CancellationToken ct)
    {
        var peek = ctx.GetGlobalOption("peek").AsPositiveInt() ?? 0;
        var bufferSize = ctx.GetGlobalOption("chunk-size").AsPositiveInt() ?? MuxDefaults.DefaultChunkSize;

        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        var stderr = Console.OpenStandardError();

        var buffer = new byte[bufferSize];
        long chunks = 0, bytes = 0;
        var format = FormatGuess.Unknown;
        var start = DateTime.UtcNow;

        int read;
        while ((read = await stdin.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
        {
            var slice = buffer.AsMemory(0, read);
            await stdout.WriteAsync(slice, ct);

            chunks++;
            bytes += read;

            if (format == FormatGuess.Unknown && chunks == 1)
            {
                format = GuessFormat(slice.Span);
            }

            if (peek > 0)
            {
                var take = Math.Min(peek, read);
                var hex = Convert.ToHexString(slice.Span[..take]);
                var line = $"[mux:inspect] chunk={chunks} bytes={read} peek={hex}\n";
                await stderr.WriteAsync(System.Text.Encoding.UTF8.GetBytes(line), ct);
            }
        }

        await stdout.FlushAsync(ct);

        var elapsed = DateTime.UtcNow - start;
        var summary = $"[mux:inspect] done chunks={chunks} bytes={bytes} format={format} elapsed={elapsed.TotalMilliseconds:F1}ms\n";
        await stderr.WriteAsync(System.Text.Encoding.UTF8.GetBytes(summary), ct);

        return 0;
    }

    private static FormatGuess GuessFormat(ReadOnlySpan<byte> sample)
    {
        if (sample.Length == 0)
        {
            return FormatGuess.Empty;
        }

        var ascii = 0;
        var newlines = 0;
        for (int i = 0; i < sample.Length; i++)
        {
            var b = sample[i];
            if (b == 0x0A)
            {
                newlines++;
            }

            if ((b >= 0x20 && b < 0x7F) || b == 0x09
                || b == 0x0A
                || b == 0x0D)
            {
                ascii++;
            }
        }
        var ratio = (double)ascii / sample.Length;
        if (ratio < 0.85)
        {
            return FormatGuess.Binary;
        }

        if (newlines > 0 && (sample[0] == (byte)'{' || sample[0] == (byte)'['))
        {
            return FormatGuess.JsonLines;
        }

        return FormatGuess.Text;
    }

    private enum FormatGuess { Unknown, Empty, Binary, Text, JsonLines }
}
