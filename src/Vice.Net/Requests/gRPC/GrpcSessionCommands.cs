using Vice.Composition;
using Vice.Lexicon;
using Vice.Network.gRPC;
using static Vice.Dsl;

namespace Vice.Network.gRPC;

[ViceCommandPack]
public static class GrpcSessionCommands
{
    public static void Register(IViceApp app, GrpcConnectionManager connections)
    {

        app.Register(
            Verbs.Grpc() > Nouns.Connect() * Targets.Endpoint,
            "Connect to a gRPC endpoint",
            async (ctx, ct) =>
            {
                var endpoint = ctx["endpoint"] ?? throw new InvalidOperationException("Target 'endpoint' not bound.");
                var plaintext = ctx.GetGlobalOption("plaintext") is not null;
                var insecure = ctx.GetGlobalOption("insecure") is not null;

                if (insecure)
                {
                    GrpcConnectionManager.WarnInsecure(ctx.Session?.Logger, endpoint);
                }

                connections.Connect(endpoint, plaintext, insecure);
                Vice.Output.Line($"Connected to {endpoint}.");
                return 0;
            });

        app.Register(
            Verbs.Grpc() > Nouns.Disconnect() * Targets.Endpoint,
            "Disconnect from a gRPC endpoint",
            async (ctx, ct) =>
            {
                var endpoint = ctx["endpoint"] ?? throw new InvalidOperationException("Target 'endpoint' not bound.");
                if (connections.Disconnect(endpoint))
                {
                    Vice.Output.Line($"Disconnected from {endpoint}.");
                }
                else
                {
                    Vice.Output.Error($"Not connected to {endpoint}.");
                }

                return 0;
            });

        app.Register(
            Verbs.Grpc() > Nouns.Connections(),
            "List active gRPC connections",
            async (ctx, ct) =>
            {
                var conns = connections.GetConnections();
                if (conns.Count == 0)
                {
                    Vice.Output.Line("No active connections.");
                    return 0;
                }

                foreach (var conn in conns)
                {
                    Vice.Output.Line($"  {conn.Endpoint,-30} connected {conn.ConnectedAt:u}  {conn.CallCount} calls");
                }

                return 0;
            });

        app.Register(
            Verbs.Grpc() > Nouns.Stream() * Targets.Method > Connectors.On() * Targets.Endpoint,
            "Start a bidirectional gRPC stream",
            async (ctx, ct) =>
            {
                if (ctx.NonInteractive)
                {
                    Vice.Output.Error("--non-interactive: refusing to start interactive bidi stream.");
                    return Vice.Execution.ViceExitCode.USAGE_ERROR;
                }

                var method = ctx["method"] ?? throw new InvalidOperationException("Target 'method' not bound.");
                var endpoint = ctx["endpoint"] ?? throw new InvalidOperationException("Target 'endpoint' not bound.");
                var bidi = new GrpcBidiSession(connections, ctx.Console, System.Console.In);
                return await bidi.RunAsync(endpoint, method, ct);
            });
    }
}
