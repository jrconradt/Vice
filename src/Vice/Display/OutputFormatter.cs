using System.Text;
using System.Text.Json;
using Vice.Contracts;
using Vice.Display.Rendering;

namespace Vice.Display;

public static class OutputFormatter
{
    public static void WriteResponse(
        byte[] data,
        OutputFormatKind format,
        Encoding encoding,
        IConsoleWriter writer)
    {
        switch (format)
        {
            case OutputFormatKind.Hex:
                {
                    WriteHexDump(data, writer);
                    break;
                }
            case OutputFormatKind.Json:
            case OutputFormatKind.Jsonl:
            case OutputFormatKind.Ndjson:
                {
                    WriteJson(data, encoding, writer);
                    break;
                }
            case OutputFormatKind.Auto:
            case OutputFormatKind.Text:
            default:
                {
                    WriteText(data, encoding, writer);
                    break;
                }
        }
    }

    private static void WriteText(byte[] data, Encoding encoding, IConsoleWriter writer)
    {
        writer.WriteLine(encoding.GetString(data));
    }

    private static void WriteHexDump(byte[] data, IConsoleWriter writer)
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

            writer.WriteLine(string.Concat(parts));
        }
    }

    private static void WriteJson(byte[] data, Encoding encoding, IConsoleWriter writer)
    {
        var payload = new OutputFormatJsonPayload(
            Bytes: data.Length,
            Text: encoding.GetString(data),
            Base64: Convert.ToBase64String(data));

        var json = JsonSerializer.Serialize(payload, OutputFormatterJsonContext.Default.OutputFormatJsonPayload);
        writer.WriteLine(json);
    }
}
