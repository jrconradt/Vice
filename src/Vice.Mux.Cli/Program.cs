using System.Net.Sockets;
using Vice.Core;
using Vice.Foundation.Execution;
using Vice.Host;
using Vice.Logging;
using Vice.Mux.Cli;
using Vice.Mux.Sinks;

var logLevel = LogLevelEnv.Resolve("vice-mux");
await using var logger = new ConsoleViceLogger(logLevel);

var host = new ViceMuxHost
{
    ConnectTcpSink = OpenTcpSink,
};

await using var app = ViceApp.Create("vice-mux", "1.0.0")
    .WithDescription("Stream inspection and multiplexing for Vice pipelines")
    .WithLogger(logger)
    .ComposeFromAttributes(host)
    .Build()
    .RegisterDiscoveredPacks(host);

using var cts = Vice.Core.Signals.HookGracefulShutdown();

try
{
    return await app.RunAsync(args, cts.Token);
}
catch (OperationCanceledException)
{
    return ViceExitCode.INTERRUPTED;
}
catch (IOException ex) when (Vice.Core.Signals.IsBrokenPipe(ex))
{
    return ViceExitCode.SUCCESS;
}

static async ValueTask<ISink> OpenTcpSink(string hostPort, CancellationToken ct, IViceLogger log)
{
    var (targetHost, port) = SinkFactory.ParseTcpEndpoint(hostPort);
    var client = new TcpClient();
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));
    try
    {
        await client.ConnectAsync(targetHost,
                                  port,
                                  timeout.Token);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        client.Dispose();
        throw new TimeoutException($"tcp sink connect to {targetHost}:{port} timed out");
    }
    catch
    {
        client.Dispose();
        throw;
    }

    client.NoDelay = true;
    return new StreamSink(client.GetStream(), $"tcp:{targetHost}:{port}", log, client);
}
