using System.Text;
using System.Text.Json;

namespace Vice.Display;

public enum NetworkOutputFormat
{
    Hex,
    Json,
    Text,
}

internal static class OutputFormatter
{
    public static void WriteResponse(byte[] data, NetworkOutputFormat format, Encoding encoding)
    {
        switch (format)
        {
            case NetworkOutputFormat.Hex:
                WriteHexDump(data);
                break;
            case NetworkOutputFormat.Json:
                WriteJson(data, encoding);
                break;
            case NetworkOutputFormat.Text:
                WriteText(data, encoding);
                break;
        }
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
