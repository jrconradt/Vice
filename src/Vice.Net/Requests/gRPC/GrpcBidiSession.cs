using Grpc.Core;
using Grpc.Net.Client;
using Vice.Logging;
using Vice.Network.gRPC;
using Vice.Display;

namespace Vice.Network.gRPC;

internal sealed class GrpcBidiSession
{
    private readonly GrpcConnectionManager _connections;
    private readonly TextReader _reader;

    public GrpcBidiSession(
        GrpcConnectionManager connections,
        IConsoleWriter console,
        TextReader reader,
        IViceLogger? logger = null)
    {
        _ = console;
        _ = logger;
        _connections = connections;
        _reader = reader;
    }

    public async Task<int> RunAsync(string endpoint, string method, CancellationToken ct)
    {
        GrpcChannel channel;
        try
        {
            channel = _connections.GetChannel(endpoint);
        }
        catch (InvalidOperationException ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, $"bidi session: not connected to {endpoint}", ex);
            Vice.Output.Error($"Not connected to {endpoint}. Use 'grpc connect {endpoint}' first.");
            return 1;
        }

        Vice.Output.Line($"Streaming to {method} (Ctrl+D or 'done' to end)");

        var invoker = channel.CreateCallInvoker();
        var serviceName = method.Contains('/') ? method.Split('/')[0] : method;
        var methodName = method.Contains('/') ? method.Split('/')[1] : method;

        var methodDef = new Method<byte[], byte[]>(
            MethodType.DuplexStreaming,
            serviceName,
            methodName,
            new Marshaller<byte[]>(x => x, x => x),
            new Marshaller<byte[]>(x => x, x => x));

        using var call = invoker.AsyncDuplexStreamingCall<byte[], byte[]>(methodDef, null, new CallOptions(cancellationToken: ct));

        int sent = 0, received = 0;

        var readTask = Task.Run(async () =>
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync(ct))
            {
                var json = System.Text.Encoding.UTF8.GetString(response);
                Vice.Output.Line($"<- {json}");
                Interlocked.Increment(ref received);
            }
        }, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                Vice.Output.Write("grpc> ");
                string? line;
                try
                {
                    line = await Task.Run(() => _reader.ReadLine(), ct).WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (line is null || line.Trim().Equals("done", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                try
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(trimmed);
                    await call.RequestStream.WriteAsync(bytes, ct);
                    sent++;
                }
                catch (Exception ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, "bidi session send failed", ex);
                    Vice.Output.Error($"Send failed: {ex.Message}");
                }
            }

            await call.RequestStream.CompleteAsync();
        }
        finally
        {
            try
            {
                await readTask;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                Vice.Log.Emit(ViceLogLevel.Trace, "bidi session reader cancelled");
            }
            catch (OperationCanceledException)
            {
                Vice.Log.Emit(ViceLogLevel.Trace, "bidi session reader cancelled");
            }
            catch (RpcException ex)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, "bidi session reader rpc fault", ex);
                Vice.Output.Error($"Stream error: {ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, "bidi session reader fault", ex);
                Vice.Output.Error($"Stream error: {ex.Message}");
            }
        }

        Vice.Output.Line($"Stream closed. {sent} sent, {received} received.");
        return 0;
    }
}
