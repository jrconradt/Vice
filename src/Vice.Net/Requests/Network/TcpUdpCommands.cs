using System.Net.Sockets;
using Vice.Composition;
using Vice.Display;
using Vice.Execution;
using Vice.Lexicon;
using Vice.Logging;
using Vice.Net.Requests.Grpc;
using static Vice.Dsl;

namespace Vice.Net.Commands.Network;

[ViceCommandPack]
public static class TcpUdpCommands
{
    private const int TcpDefaultTimeoutMs = 30000;
    private const int UdpDefaultTimeoutMs = 5000;
    private const int MaxResponseBytes = 16 * 1024 * 1024;

    public static void Register(IViceApp app)
    {
        app.Register(
            Verbs.Tcp() > Nouns.Send() * Targets.Data > Connectors.To() > Nouns.Endpoint() * Targets.Endpoint,
            "Send a payload over TCP and print the response",
            (ctx, ct) => RunTcpAsync(ctx, "data", ct));

        app.Register(
            Verbs.Tcp() > Nouns.Send() > Nouns.File() * Targets.Path > Connectors.To() > Nouns.Endpoint() * Targets.Endpoint,
            "Send a file over TCP and print the response",
            (ctx, ct) => RunTcpAsync(ctx, "path", ct));

        app.Register(
            Verbs.Udp() > Nouns.Send() * Targets.Data > Connectors.To() > Nouns.Endpoint() * Targets.Endpoint,
            "Send a UDP datagram and (by default) print one reply",
            (ctx, ct) => RunUdpAsync(ctx, "data", ct));

        app.Register(
            Verbs.Udp() > Nouns.Send() > Nouns.File() * Targets.Path > Connectors.To() > Nouns.Endpoint() * Targets.Endpoint,
            "Send a file as a UDP datagram and (by default) print one reply",
            (ctx, ct) => RunUdpAsync(ctx, "path", ct));
    }

    private static byte[] ResolvePayload(CommandContext ctx, string targetName)
    {
        if (targetName == "path")
        {
            var path = ctx.GetFullPath("path");
            return File.ReadAllBytes(path);
        }

        var data = ctx.Require("data");
        var encoding = NetworkOptions.GetEncoding(ctx);
        return encoding.GetBytes(data);
    }

    private static bool TryResolveSendArgs(CommandContext ctx,
                                           string payloadTarget,
                                           int defaultTimeoutMs,
                                           out string host,
                                           out int port,
                                           out int timeoutMs,
                                           out OutputFormatKind format,
                                           out byte[] payload,
                                           out int exitCode)
    {
        host = string.Empty;
        port = 0;
        timeoutMs = defaultTimeoutMs;
        format = OutputFormatKind.Text;
        payload = Array.Empty<byte>();
        exitCode = ViceExitCode.SUCCESS;
        try
        {
            (host, port) = NetworkOptions.ParseEndpoint(ctx.Require("endpoint"));
            timeoutMs = NetworkOptions.GetTimeout(ctx, defaultTimeoutMs);
            format = NetworkOptions.GetFormat(ctx);
            payload = ResolvePayload(ctx, payloadTarget);
            return true;
        }
        catch (ArgumentException ex)
        {
            Vice.Output.Error(ex.Message);
            exitCode = ViceExitCode.USAGE_ERROR;
            return false;
        }
        catch (IOException ex)
        {
            Vice.Output.Error(ex.Message);
            exitCode = ViceExitCode.FAILURE;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Vice.Output.Error(ex.Message);
            exitCode = ViceExitCode.FAILURE;
            return false;
        }
    }

    internal static async Task<int> RunTcpAsync(CommandContext ctx,
                                                string payloadTarget,
                                                CancellationToken ct)
    {
        if (!TryResolveSendArgs(ctx,
                                payloadTarget,
                                TcpDefaultTimeoutMs,
                                out var host,
                                out var port,
                                out var timeoutMs,
                                out var format,
                                out var payload,
                                out var exitCode))
        {
            return exitCode;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            var addresses = await SafeOutboundConnection.CheckEndpointAsync(host, timeoutCts.Token);

            using var client = new TcpClient();
            await client.ConnectAsync(addresses, port, timeoutCts.Token);

            var stream = client.GetStream();
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);
            client.Client.Shutdown(SocketShutdown.Send);

            var response = await ReadAllAsync(stream, timeoutCts.Token);
            OutputFormatter.WriteResponse(response, format, NetworkOptions.GetEncoding(ctx));
            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Vice.Output.Error($"TCP timeout after {timeoutMs} ms connecting to {host}:{port}.");
            return ViceExitCode.FAILURE;
        }
        catch (OperationCanceledException)
        {
            return ViceExitCode.INTERRUPTED;
        }
        catch (SafeNetBlockedException ex)
        {
            Vice.Output.Error(ex.Message);
            return ViceExitCode.FAILURE;
        }
        catch (SocketException ex)
        {
            return CommandErrorHandler.Handle(ctx, new SocketFailure("tcp", ex));
        }
        catch (IOException ex)
        {
            Vice.Output.Error($"TCP error talking to {host}:{port}: {ex.Message}");
            return ViceExitCode.FAILURE;
        }
    }

    internal static async Task<int> RunUdpAsync(CommandContext ctx,
                                                string payloadTarget,
                                                CancellationToken ct)
    {
        if (!TryResolveSendArgs(ctx,
                                payloadTarget,
                                UdpDefaultTimeoutMs,
                                out var host,
                                out var port,
                                out var timeoutMs,
                                out var format,
                                out var payload,
                                out var exitCode))
        {
            return exitCode;
        }

        var noReply = ctx.HasGlobalOption("no-reply");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            var addresses = await SafeOutboundConnection.CheckEndpointAsync(host, timeoutCts.Token);

            using var client = new UdpClient();
            await client.Client.ConnectAsync(addresses, port, timeoutCts.Token);
            await client.Client.SendAsync(payload, SocketFlags.None, timeoutCts.Token);

            if (noReply)
            {
                return ViceExitCode.SUCCESS;
            }

            var result = await client.ReceiveAsync(timeoutCts.Token);
            OutputFormatter.WriteResponse(result.Buffer, format, NetworkOptions.GetEncoding(ctx));
            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Vice.Output.Error($"UDP timeout after {timeoutMs} ms waiting for {host}:{port}.");
            return ViceExitCode.FAILURE;
        }
        catch (OperationCanceledException)
        {
            return ViceExitCode.INTERRUPTED;
        }
        catch (SafeNetBlockedException ex)
        {
            Vice.Output.Error(ex.Message);
            return ViceExitCode.FAILURE;
        }
        catch (SocketException ex)
        {
            return CommandErrorHandler.Handle(ctx, new SocketFailure("udp", ex));
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[81920];
        using var collected = new MemoryStream();
        while (collected.Length < MaxResponseBytes)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                break;
            }

            var room = MaxResponseBytes - (int)collected.Length;
            var take = Math.Min(read, room);
            await collected.WriteAsync(buffer.AsMemory(0, take), ct);
        }

        return collected.ToArray();
    }
}
