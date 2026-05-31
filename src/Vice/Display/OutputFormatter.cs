using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using Vice.Execution;

namespace Vice.Display;

public static class OutputFormatter
{
    private static readonly FrozenDictionary<OutputFormatKind, Action<byte[], Encoding>> Renderers =
        new Dictionary<OutputFormatKind, Action<byte[], Encoding>>
        {
            [OutputFormatKind.Auto] = WriteText,
            [OutputFormatKind.Text] = WriteText,
            [OutputFormatKind.Hex] = (data, _) => WriteHexDump(data),
            [OutputFormatKind.Json] = WriteJson,
            [OutputFormatKind.Jsonl] = WriteJson,
            [OutputFormatKind.Ndjson] = WriteJson,
        }.ToFrozenDictionary();

    public static void WriteResponse(byte[] data, OutputFormatKind format, Encoding encoding)
    {
        if (!Renderers.TryGetValue(format, out var render))
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
