using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Vice.Execution;

namespace Vice.Display;

public delegate void OutputRenderer(byte[] data, Encoding encoding);

public static class OutputFormatter
{
    private static readonly ConcurrentDictionary<OutputFormatKind, OutputRenderer> _renderers = new();

    static OutputFormatter()
    {
        Register(OutputFormatKind.Auto, WriteText);
        Register(OutputFormatKind.Text, WriteText);
        Register(OutputFormatKind.Hex, static (data, _) => WriteHexDump(data));
        Register(OutputFormatKind.Json, WriteJson);
        Register(OutputFormatKind.Jsonl, WriteJson);
        Register(OutputFormatKind.Ndjson, WriteJson);
    }

    public static void Register(OutputFormatKind format, OutputRenderer renderer)
    {
        if (renderer is null)
        {
            throw new ArgumentNullException(nameof(renderer));
        }

        _renderers[format] = renderer;
    }

    public static void WriteResponse(byte[] data, OutputFormatKind format, Encoding encoding)
    {
        if (!_renderers.TryGetValue(format, out var render))
        {
            render = WriteText;
        }

        render(data, encoding);
    }

    private static void WriteText(byte[] data, Encoding encoding)
    {
        Vice.Output.Line(encoding.GetString(data));
    }

    private static void WriteHexDump(byte[] data)
    {
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            var parts = new List<string> { $"{offset:X8}  " };

            var count = Math.Min(16, data.Length - offset);
            for (var i = 0; i < 16; i++)
            {
                parts.Add(i < count ? $"{data[offset + i]:X2} " : "   ");

                if (i == 7)
                {
                    parts.Add(" ");
                }
            }

            parts.Add(" |");
            for (var i = 0; i < count; i++)
            {
                var b = data[offset + i];
                parts.Add($"{(b is >= 32 and <= 126 ? (char)b : '.')}");
            }
            parts.Add("|");

            Vice.Output.Line(string.Concat(parts));
        }
    }

    private static void WriteJson(byte[] data, Encoding encoding)
    {
        var payload = new OutputFormatJsonPayload(
            Bytes: data.Length,
            Text: encoding.GetString(data),
            Base64: Convert.ToBase64String(data));

        var json = JsonSerializer.Serialize(payload, OutputFormatterJsonContext.Default.OutputFormatJsonPayload);
        Vice.Output.Line(json);
    }
}
