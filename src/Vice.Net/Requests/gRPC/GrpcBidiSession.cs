using Grpc.Core;
using Grpc.Net.Client;
using Vice.Logging;
using Vice.Network.gRPC;
using Vice.Display;

namespace Vice.Network.gRPC;

internal sealed class GrpcBidiSession
{
    private const int MaxReconnectAttempts = 3;

    private static readonly TimeSpan CallDeadline = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan ReconnectBackoff = TimeSpan.FromSeconds(1);

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
        var methodDef = BuildMethod(method);
        Vice.Output.Line($"Streaming to {method} (Ctrl+D or 'done' to end)");

        var totalSent = 0;
        var totalReceived = 0;
        var attempt = 0;
        var exitCode = 0;

        while (true)
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
                exitCode = Vice.Execution.ViceExitCode.FAILURE;
                break;
            }

            var leg = await RunLegAsync(channel, methodDef, ct).ConfigureAwait(false);
            totalSent += leg.Sent;
            totalReceived += leg.Received;

            if (leg.HardFault)
            {
                exitCode = Vice.Execution.ViceExitCode.FAILURE;
                break;
            }

            if (leg.UserEnded
                || !leg.TransientFault)
            {
                break;
            }

            if (ct.IsCancellationRequested)
            {
                exitCode = Vice.Execution.ViceExitCode.FAILURE;
                break;
            }

            attempt++;
            if (attempt > MaxReconnectAttempts)
            {
                Vice.Output.Error($"Connection lost; gave up after {MaxReconnectAttempts} reconnect attempts.");
                exitCode = Vice.Execution.ViceExitCode.FAILURE;
                break;
            }

            Vice.Output.Error($"Connection lost, reconnecting (attempt {attempt}/{MaxReconnectAttempts})...");
            if (!await TryReestablishAsync(endpoint, ct).ConfigureAwait(false))
            {
                Vice.Output.Error($"Reconnect to {endpoint} failed.");
                exitCode = Vice.Execution.ViceExitCode.FAILURE;
                break;
            }
        }

        Vice.Output.Line($"Stream closed. {totalSent} sent, {totalReceived} received.");
        return exitCode;
    }

    private async Task<LegResult> RunLegAsync(
        GrpcChannel channel,
        Method<byte[], byte[]> methodDef,
        CancellationToken ct)
    {
        var invoker = channel.CreateCallInvoker();
        var callOptions = new CallOptions(cancellationToken: ct)
            .WithDeadline(DateTime.UtcNow.Add(CallDeadline));

        using var call = invoker.AsyncDuplexStreamingCall<byte[], byte[]>(methodDef, null, callOptions);

        var sent = 0;
        var received = 0;
        var transientFault = false;
        var hardFault = false;
        var userEnded = false;

        var readTask = Task.Run(async () =>
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync(ct))
            {
                var json = System.Text.Encoding.UTF8.GetString(response);
                Vice.Output.Line($"<- {json}");
                Interlocked.Increment(ref received);
            }
        }, ct);

        var writeFaulted = false;
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

                if (line is null
                    || line.Trim().Equals("done", StringComparison.OrdinalIgnoreCase))
                {
                    userEnded = true;
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
                catch (RpcException ex) when (IsTransient(ex))
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, "bidi session send hit transient fault", ex);
                    transientFault = true;
                    writeFaulted = true;
                    break;
                }
                catch (Exception ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, "bidi session send failed", ex);
                    Vice.Output.Error($"Send failed: {ex.Message}");
                }
            }

            if (!writeFaulted)
            {
                await call.RequestStream.CompleteAsync();
            }
        }
        catch (RpcException ex) when (IsTransient(ex))
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "bidi session request stream hit transient fault", ex);
            transientFault = true;
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
            catch (RpcException ex) when (IsTransient(ex))
            {
                transientFault = true;
                Vice.Log.Emit(ViceLogLevel.Warn, "bidi session reader transient fault", ex);
            }
            catch (RpcException ex)
            {
                hardFault = true;
                Vice.Log.Emit(ViceLogLevel.Warn, "bidi session reader rpc fault", ex);
                Vice.Output.Error($"Stream error: {ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                hardFault = true;
                Vice.Log.Emit(ViceLogLevel.Warn, "bidi session reader fault", ex);
                Vice.Output.Error($"Stream error: {ex.Message}");
            }
        }

        return new LegResult(sent, received, transientFault, hardFault, userEnded);
    }

    private async Task<bool> TryReestablishAsync(string endpoint, CancellationToken ct)
    {
        try
        {
            await Task.Delay(ReconnectBackoff, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        try
        {
            _connections.GetChannel(endpoint);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, $"bidi session: reconnect to {endpoint} found no channel", ex);
            return false;
        }
    }

    private static Method<byte[], byte[]> BuildMethod(string method)
    {
        var serviceName = method.Contains('/') ? method.Split('/')[0] : method;
        var methodName = method.Contains('/') ? method.Split('/')[1] : method;

        return new Method<byte[], byte[]>(
            MethodType.DuplexStreaming,
            serviceName,
            methodName,
            new Marshaller<byte[]>(x => x, x => x),
            new Marshaller<byte[]>(x => x, x => x));
    }

    private static bool IsTransient(RpcException ex)
    {
        return ex.StatusCode == StatusCode.Unavailable
            || ex.StatusCode == StatusCode.DeadlineExceeded
            || ex.StatusCode == StatusCode.Internal;
    }

    private readonly record struct LegResult(int Sent,
                                             int Received,
                                             bool TransientFault,
                                             bool HardFault,
                                             bool UserEnded);
}
