using System.Buffers.Binary;
using System.Text.Json;
using Vice.Contracts;
using Vice.Session;

namespace Vice.Ipc;

internal static class PipeProtocol
{
    public const int MAX_MESSAGE_BYTES = 16 * 1024 * 1024;

    public static async Task WriteMessageAsync(Stream stream, PipeMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, PipeMessageJsonContext.Default.PipeMessage);

        if (json.Length > MAX_MESSAGE_BYTES)
        {
            throw new ArgumentException(
                $"Pipe payload length {json.Length} exceeds MAX_MESSAGE_BYTES {MAX_MESSAGE_BYTES}.",
                nameof(message));
        }

        var header = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), SessionState.ProtocolVersion);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), json.Length);

        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(json, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<PipeMessage?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[8];
        var bytesRead = await ReadExactlyAsync(stream, header, ct).ConfigureAwait(false);

        if (bytesRead == 0)
        {
            return null;
        }

        if (bytesRead < 8)
        {
            throw new InvalidOperationException("Unexpected end of stream while reading message header.");
        }

        var peerVersion = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));

        if (peerVersion != SessionState.ProtocolVersion)
        {
            throw new IOException(
                $"Pipe protocol version mismatch: peer sent v{peerVersion}, this process speaks v{SessionState.ProtocolVersion}.");
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 4));

        if (length < 0 || length > MAX_MESSAGE_BYTES)
        {
            throw new IOException(
                $"Pipe payload length {length} out of bounds [0, {MAX_MESSAGE_BYTES}]");
        }

        var body = new byte[length];

        bytesRead = await ReadExactlyAsync(stream, body, ct).ConfigureAwait(false);

        if (bytesRead < length)
        {
            throw new InvalidOperationException("Unexpected end of stream while reading message body.");
        }

        try
        {
            return JsonSerializer.Deserialize(body, PipeMessageJsonContext.Default.PipeMessage);
        }
        catch (NotSupportedException ex)
        {
            throw new JsonException("Pipe payload was not a recognized message.", ex);
        }
    }

    private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead), ct).ConfigureAwait(false);

            if (read == 0)
            {
                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }
}
