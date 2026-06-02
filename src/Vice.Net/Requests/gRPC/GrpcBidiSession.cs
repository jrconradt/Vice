using Grpc.Core;
using Vice.Display.Rendering;
using Vice.Logging;
using Vice.Net.Requests.Grpc;

namespace Vice.Net.Requests.Grpc;

internal sealed class GrpcBidiSession
{
    private const int MaxReconnectAttempts = 3;

    private static readonly TimeSpan CallDeadline = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan BaseReconnectBackoff = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaxReconnectBackoff = TimeSpan.FromSeconds(30);

    private readonly GrpcConnectionManager _connections;
    private readonly IConsoleWriter _console;
    private readonly TextReader _reader;
    private readonly IViceLogger _logger;

    public GrpcBidiSession(
        GrpcConnectionManager connections,
        IConsoleWriter console,
        TextReader reader,
        IViceLogger? logger = null)
    {
        _connections = connections;
        _console = console;
        _reader = reader;
        _logger = logger ?? NullViceLogger.Instance;
    }

    public async Task<int> RunAsync(string endpoint, string method, CancellationToken ct)
    {
        var methodDef = BuildMethod(method);
        _console.WriteLine($"Streaming to {method} (Ctrl+D or 'done' to end)");

        var totalSent = 0;
        var totalReceived = 0;
        var attempt = 0;
        var exitCode = 0;

        while (true)
        {
            GrpcConnectionManager.ConnectionLease lease;
            try
            {
                lease = _connections.LeaseChannel(endpoint);
            }
            catch (InvalidOperationException ex)
            {
                _logger.Log(ViceLogLevel.Warn, $"bidi session: not connected to {endpoint}", ex);
                _console.WriteError($"Not connected to {endpoint}. Use 'grpc connect {endpoint}' first.");
                exitCode = Vice.Execution.ViceExitCode.FAILURE;
                break;
            }

            LegResult leg;
            using (lease)
            {
                leg = await RunLegAsync(lease, methodDef, ct).ConfigureAwait(false);
            }

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
                _console.WriteError($"Connection lost; gave up after {MaxReconnectAttempts} reconnect attempts.");
                exitCode = Vice.Execution.ViceExitCode.FAILURE;
                break;
            }

            _console.WriteError($"Connection lost, reconnecting (attempt {attempt}/{MaxReconnectAttempts})...");
            if (!await TryReestablishAsync(endpoint, attempt, ct).ConfigureAwait(false))
            {
                _console.WriteError($"Reconnect to {endpoint} failed.");
                exitCode = Vice.Execution.ViceExitCode.FAILURE;
                break;
            }
        }

        _console.WriteLine($"Stream closed. {totalSent} sent, {totalReceived} received.");
        return exitCode;
    }

    private async Task<LegResult> RunLegAsync(
        GrpcConnectionManager.ConnectionLease lease,
        Method<byte[], byte[]> methodDef,
        CancellationToken ct)
    {
        var invoker = lease.Channel.CreateCallInvoker();
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
                lease.Renew();
                var json = System.Text.Encoding.UTF8.GetString(response);
                _console.WriteLine($"<- {json}");
                Interlocked.Increment(ref received);
            }
        }, ct);

        var writeFaulted = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                _console.Write("grpc> ");
                string? line;
                try
                {
                    line = await Task.Factory.StartNew(() => _reader.ReadLine(),
                                                       ct,
                                                       TaskCreationOptions.LongRunning,
                                                       TaskScheduler.Default).WaitAsync(ct).ConfigureAwait(false);
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
                    lease.Renew();
                    sent++;
                }
                catch (RpcException ex) when (IsTransient(ex))
                {
                    _logger.Log(ViceLogLevel.Warn, "bidi session send hit transient fault", ex);
                    transientFault = true;
                    writeFaulted = true;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Log(ViceLogLevel.Warn, "bidi session send failed", ex);
                    _console.WriteError($"Send failed: {ex.Message}");
                }
            }

            if (!writeFaulted)
            {
                await call.RequestStream.CompleteAsync();
            }
        }
        catch (RpcException ex) when (IsTransient(ex))
        {
            _logger.Log(ViceLogLevel.Warn, "bidi session request stream hit transient fault", ex);
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
                _logger.Log(ViceLogLevel.Trace, "bidi session reader cancelled");
            }
            catch (OperationCanceledException)
            {
                _logger.Log(ViceLogLevel.Trace, "bidi session reader cancelled");
            }
            catch (RpcException ex) when (IsTransient(ex))
            {
                transientFault = true;
                _logger.Log(ViceLogLevel.Warn, "bidi session reader transient fault", ex);
            }
            catch (RpcException ex)
            {
                hardFault = true;
                _logger.Log(ViceLogLevel.Warn, "bidi session reader rpc fault", ex);
                _console.WriteError($"Stream error: {ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                hardFault = true;
                _logger.Log(ViceLogLevel.Warn, "bidi session reader fault", ex);
                _console.WriteError($"Stream error: {ex.Message}");
            }
        }

        return new LegResult(sent, received, transientFault, hardFault, userEnded);
    }

    private async Task<bool> TryReestablishAsync(string endpoint, int attempt, CancellationToken ct)
    {
        try
        {
            await Task.Delay(ComputeReconnectBackoff(attempt), ct).ConfigureAwait(false);
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
            _logger.Log(ViceLogLevel.Warn, $"bidi session: reconnect to {endpoint} found no channel", ex);
            return false;
        }
    }

    private static TimeSpan ComputeReconnectBackoff(int attempt)
    {
        var scaled = BaseReconnectBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(scaled, MaxReconnectBackoff.TotalMilliseconds);
        var jittered = capped * (0.5 + Random.Shared.NextDouble() * 0.5);
        return TimeSpan.FromMilliseconds(jittered);
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
