using Vice.Composition;
using Vice.Lexicon;
using Vice.Net.Requests.Grpc;
using static Vice.Dsl;

namespace Vice.Net.Requests.Grpc;

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

                connections.Connect(endpoint, plaintext, ctx.Console);
                ctx.Console.WriteLine($"Connected to {endpoint}.");
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
                    ctx.Console.WriteLine($"Disconnected from {endpoint}.");
                }
                else
                {
                    ctx.Console.WriteError($"Not connected to {endpoint}.");
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
                    ctx.Console.WriteLine("No active connections.");
                    return 0;
                }

                foreach (var conn in conns)
                {
                    ctx.Console.WriteLine($"  {conn.Endpoint,-30} connected {conn.ConnectedAt:u}");
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
                    ctx.Console.WriteError("--non-interactive: refusing to start interactive bidi stream.");
                    return Vice.Foundation.Execution.ViceExitCode.USAGE_ERROR;
                }

                var method = ctx["method"] ?? throw new InvalidOperationException("Target 'method' not bound.");
                var endpoint = ctx["endpoint"] ?? throw new InvalidOperationException("Target 'endpoint' not bound.");
                var bidi = new GrpcBidiSession(connections, ctx.Console, System.Console.In);
                return await bidi.RunAsync(endpoint, method, ct);
            });
    }
}
