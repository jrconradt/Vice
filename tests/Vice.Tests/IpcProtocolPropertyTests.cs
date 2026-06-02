using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Vice.Contracts;
using Vice.Ipc;
using Vice.Logging;
using Xunit;

namespace Vice.Tests;

public class IpcProtocolPropertyTests
{
    private const long ITERATIONS = 5_000;

    private static readonly Gen<byte[]> ArbitraryBody =
        Gen.Frequency(
            (3, Gen.Byte.Array[0, 256]),
            (2, ValidFrameBodyGen()),
            (1, Gen.OneOfConst(
                Encoding.UTF8.GetBytes("{ not json"),
                Encoding.UTF8.GetBytes("{\"type\":\"unknown\"}"),
                Encoding.UTF8.GetBytes("[]"),
                Encoding.UTF8.GetBytes("null"),
                Encoding.UTF8.GetBytes("{}"),
                System.Array.Empty<byte>())));

    private static readonly Gen<byte[]> ArbitraryFrame =
        Gen.Frequency(
            (3, FramedGen()),
            (2, RawBytesGen()),
            (1, TruncatedFrameGen()),
            (1, OversizedPrefixGen()));

    [Fact]
    public void ReadMessage_OverArbitraryFrames_OnlyThrowsGuardedExceptions()
    {
        ArbitraryFrame.Sample(frame =>
            {
                try
                {
                    using var stream = new MemoryStream(frame, writable: false);
                    var message = PipeProtocol.ReadMessageAsync(stream, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    Assert.True(message is null || message is PipeMessage);
                }
                catch (System.Exception ex) when (ex is IOException
                                                  or InvalidOperationException
                                                  or JsonException)
                {
                }
            },
            iter: ITERATIONS,
            seed: "0000IpcReadFrame00");
    }

    [Fact]
    public void ReadMessage_OverArbitraryBodies_OnlyThrowsGuardedExceptions()
    {
        ArbitraryBody.Sample(body =>
            {
                var frame = WithLengthPrefix(body, body.Length);

                try
                {
                    using var stream = new MemoryStream(frame, writable: false);
                    var message = PipeProtocol.ReadMessageAsync(stream, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    Assert.True(message is null || message is PipeMessage);
                }
                catch (System.Exception ex) when (ex is IOException
                                                  or InvalidOperationException
                                                  or JsonException)
                {
                }
            },
            iter: ITERATIONS,
            seed: "0000IpcReadBody00");
    }

    [Fact]
    public async Task ReadMessage_RoundTripsWrittenMessages()
    {
        var pipeName = "vice-test-" + System.Guid.NewGuid().ToString("N");
        var server = new PipeServer(pipeName, (msg, ct) =>
        {
            if (msg is CommandMessage cmd)
            {
                return Task.FromResult<PipeMessage?>(new CommandResponse
                {
                    ExitCode = 0,
                    Output = "echo:" + cmd.CommandLine,
                });
            }
            return Task.FromResult<PipeMessage?>(null);
        }, NullViceLogger.Instance);

        using var serverCts = new CancellationTokenSource();
        await server.StartAsync(serverCts.Token);

        await using var client = await PipeClient.TryConnectAsync(pipeName, timeoutMs: 2000);
        Assert.NotNull(client);

        var resp = await client!.SendAsync(new CommandMessage { CommandLine = "ping" }, CancellationToken.None);
        var cr = Assert.IsType<CommandResponse>(resp);
        Assert.Equal("echo:ping", cr.Output);

        serverCts.Cancel();
        await server.DisposeAsync();
    }

    private static Gen<byte[]> ValidFrameBodyGen()
    {
        return Gen.OneOf(
            Gen.String[Gen.Char[(char)32, (char)126], 0, 32].Select(line =>
                JsonSerializer.SerializeToUtf8Bytes(
                    new CommandMessage { CommandLine = line },
                    PipeMessageJsonContext.Default.PipeMessage)),
            Gen.Const(JsonSerializer.SerializeToUtf8Bytes(
                new JobStatusRequest(),
                PipeMessageJsonContext.Default.PipeMessage)));
    }

    private static Gen<byte[]> FramedGen()
    {
        return ArbitraryBody.Select(body => WithLengthPrefix(body, body.Length));
    }

    private static Gen<byte[]> RawBytesGen()
    {
        return Gen.Byte.Array[0, 512];
    }

    private static Gen<byte[]> TruncatedFrameGen()
    {
        return Gen.Select(
            Gen.Byte.Array[1, 128],
            Gen.Int[1, 64],
            (body, declaredExtra) =>
            {
                var declaredLength = body.Length + declaredExtra;
                return WithLengthPrefix(body, declaredLength);
            });
    }

    private static Gen<byte[]> OversizedPrefixGen()
    {
        return Gen.OneOfConst(
            -1,
            int.MinValue,
            PipeProtocol.MAX_MESSAGE_BYTES + 1,
            int.MaxValue).Select(declaredLength =>
                WithLengthPrefix(System.Array.Empty<byte>(), declaredLength));
    }

    private static byte[] WithLengthPrefix(byte[] body, int declaredLength)
    {
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), Vice.Contracts.SessionState.ProtocolVersion);
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4, 4), declaredLength);
        body.CopyTo(frame.AsSpan(8));
        return frame;
    }
}
