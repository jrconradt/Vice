using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Reflection.V1Alpha;
using Vice.Composition;
using Vice.Contracts;
using Vice.Core;
using Vice.Foundation.Execution;
using Vice.Lexicon;
using Vice.Logging;
using Vice.Net.Commands.Network;
using static Vice.Core.Dsl;

namespace Vice.Net.Requests.Grpc;

[ViceCommandPack]
public static class GrpcReflectionCommands
{
    private const int DefaultTimeoutMs = 30000;

    public static void Register(IViceApp app, GrpcConnectionManager connections)
    {
        app.Register(
            Verbs.Grpc() > Nouns.List() > Nouns.Services() > Connectors.On() * Targets.Endpoint,
            "List the gRPC services exposed via server reflection",
            (ctx, ct) => ListServicesAsync(connections, ctx, ct));

        app.Register(
            Verbs.Grpc() > Nouns.Describe() > Nouns.Service() * Targets.Service > Connectors.On() * Targets.Endpoint,
            "Describe the rpc methods of a gRPC service via server reflection",
            (ctx, ct) => DescribeServiceAsync(connections, ctx, ct));

        app.Register(
            Verbs.Grpc() > Nouns.Call() * Targets.Method > Connectors.On() * Targets.Endpoint > Connectors.With() > Nouns.Data() * Targets.DataOptional,
            "Invoke a gRPC method (unary, server-, client-, or duplex-streaming) via server reflection",
            (ctx, ct) => CallAsync(connections, ctx, ct));
    }

    private static async Task<int> ListServicesAsync(
        GrpcConnectionManager connections,
        CommandContext ctx,
        CancellationToken ct)
    {
        var endpoint = ctx["endpoint"] ?? throw new InvalidOperationException("Target 'endpoint' not bound.");
        try
        {
            var channel = OpenChannel(connections, ctx, endpoint);
            var services = await FetchServiceNamesAsync(channel, endpoint, GetTimeout(ctx), ct).ConfigureAwait(false);
            if (ctx.WantsJson)
            {
                var quoted = services.Select(s => $"\"{JsonEncodedText.Encode(s)}\"");
                ctx.Console.WriteLine($"[{string.Join(",", quoted)}]");
                return ViceExitCode.SUCCESS;
            }

            if (services.Count == 0)
            {
                ctx.Console.WriteLine("No services exposed via reflection.");
                return ViceExitCode.SUCCESS;
            }

            foreach (var service in services)
            {
                ctx.Console.WriteLine(Sanitize(service));
            }

            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Interrupted(ctx, endpoint);
        }
        catch (Exception ex)
        {
            return Fail(ctx, endpoint, ex);
        }
    }

    private static async Task<int> DescribeServiceAsync(
        GrpcConnectionManager connections,
        CommandContext ctx,
        CancellationToken ct)
    {
        var service = ctx["service"] ?? throw new InvalidOperationException("Target 'service' not bound.");
        var endpoint = ctx["endpoint"] ?? throw new InvalidOperationException("Target 'endpoint' not bound.");
        try
        {
            var channel = OpenChannel(connections, ctx, endpoint);
            var pool = await ResolveBySymbolAsync(channel, endpoint, service, GetTimeout(ctx), ct).ConfigureAwait(false);
            var descriptor = FindService(pool, service);
            if (descriptor is null)
            {
                ctx.Console.WriteError($"Service '{service}' not found on {endpoint}.");
                return ViceExitCode.FAILURE;
            }

            if (ctx.WantsJson)
            {
                ctx.Console.WriteLine(DescribeServiceJson(descriptor));
                return ViceExitCode.SUCCESS;
            }

            ctx.Console.WriteLine($"service {Sanitize(descriptor.FullName)} {{");
            foreach (var method in descriptor.Methods)
            {
                var clientStream = method.IsClientStreaming ? "stream " : string.Empty;
                var serverStream = method.IsServerStreaming ? "stream " : string.Empty;
                ctx.Console.WriteLine($"  rpc {Sanitize(method.Name)}({clientStream}{Sanitize(method.InputType.FullName)}) returns ({serverStream}{Sanitize(method.OutputType.FullName)});");
            }

            ctx.Console.WriteLine("}");
            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Interrupted(ctx, endpoint);
        }
        catch (Exception ex)
        {
            return Fail(ctx, endpoint, ex);
        }
    }

    private static async Task<int> CallAsync(
        GrpcConnectionManager connections,
        CommandContext ctx,
        CancellationToken ct)
    {
        var method = ctx["method"] ?? throw new InvalidOperationException("Target 'method' not bound.");
        var endpoint = ctx["endpoint"] ?? throw new InvalidOperationException("Target 'endpoint' not bound.");
        var payload = ctx["data"] ?? "{}";
        if (!GrpcMethodPath.TryParse(method, out var path))
        {
            ctx.Console.WriteError($"Invalid method '{Sanitize(method)}'. Expected: package.Service/Method");
            return ViceExitCode.USAGE_ERROR;
        }

        var serviceName = path.ServiceName;
        var methodName = path.MethodName;
        try
        {
            var channel = OpenChannel(connections, ctx, endpoint);
            var timeoutMs = GetTimeout(ctx);
            var pool = await ResolveBySymbolAsync(channel, endpoint, serviceName, timeoutMs, ct).ConfigureAwait(false);
            var service = FindService(pool, serviceName);
            var methodDescriptor = service?.FindMethodByName(methodName);
            if (methodDescriptor is null)
            {
                ctx.Console.WriteError($"Method '{methodName}' not found on service '{serviceName}'.");
                return ViceExitCode.FAILURE;
            }

            var requests = BuildRequestMessages(payload, methodDescriptor);
            var metadata = BuildMetadata(ctx);
            return await DispatchAsync(channel,
                                       serviceName,
                                       methodName,
                                       methodDescriptor,
                                       requests,
                                       metadata,
                                       timeoutMs,
                                       ctx,
                                       ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Interrupted(ctx, endpoint);
        }
        catch (Exception ex)
        {
            return Fail(ctx, endpoint, ex);
        }
    }

    private static GrpcChannel OpenChannel(
        GrpcConnectionManager connections,
        CommandContext ctx,
        string endpoint)
    {
        var plaintext = ctx.GetGlobalOption("plaintext") is not null;
        return connections.Connect(endpoint, plaintext, ctx.Console);
    }

    private static int GetTimeout(CommandContext ctx)
    {
        var value = ctx.GetGlobalOption("timeout");
        if (value is null)
        {
            return DefaultTimeoutMs;
        }

        if (!int.TryParse(value, out var ms)
            || ms <= 0)
        {
            throw new BadArgument($"Invalid timeout value: '{value}'");
        }

        return ms;
    }

    private static CallOptions CallOptionsFor(
        Metadata? metadata,
        int timeoutMs,
        CancellationToken ct)
    {
        return new CallOptions(
            headers: metadata,
            deadline: DateTime.UtcNow.AddMilliseconds(timeoutMs),
            cancellationToken: ct);
    }

    private static Metadata? BuildMetadata(CommandContext ctx)
    {
        var raw = ctx.GetGlobalOption("metadata");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new BadArgument($"Invalid --metadata JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new BadArgument("--metadata must be a JSON object of string to string.");
            }

            var metadata = new Metadata();
            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                metadata.Add(entry.Name, entry.Value.ValueKind == JsonValueKind.String
                    ? entry.Value.GetString() ?? string.Empty
                    : entry.Value.GetRawText());
            }

            return metadata;
        }
    }

    private static IReadOnlyList<byte[]> BuildRequestMessages(string payload, MethodDescriptor method)
    {
        var inputType = method.InputType;
        if (method.IsClientStreaming)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "[]" : payload);
            }
            catch (JsonException ex)
            {
                throw new BadArgument($"Invalid --payload JSON: {ex.Message}", ex);
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new BadArgument("Client-streaming methods require a JSON array; each element is one message.");
                }

                var messages = new List<byte[]>();
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    try
                    {
                        messages.Add(ProtobufJsonTranscoder.JsonToProtobuf(element.GetRawText(), inputType));
                    }
                    catch (JsonException ex)
                    {
                        throw new BadArgument($"Invalid --payload JSON: {ex.Message}", ex);
                    }
                }

                return messages;
            }
        }

        try
        {
            return new[] { ProtobufJsonTranscoder.JsonToProtobuf(payload, inputType) };
        }
        catch (JsonException ex)
        {
            throw new BadArgument($"Invalid --payload JSON: {ex.Message}", ex);
        }
    }

    private static async Task<int> DispatchAsync(
        GrpcChannel channel,
        string serviceName,
        string methodName,
        MethodDescriptor descriptor,
        IReadOnlyList<byte[]> requests,
        Metadata? metadata,
        int timeoutMs,
        CommandContext ctx,
        CancellationToken ct)
    {
        var invoker = channel.CreateCallInvoker();
        var methodType = MethodTypeFor(descriptor);
        var grpcMethod = new Method<byte[], byte[]>(
            methodType,
            serviceName,
            methodName,
            ByteArrayMarshaller,
            ByteArrayMarshaller);
        var options = CallOptionsFor(metadata, timeoutMs, ct);

        if (descriptor.IsClientStreaming
            && descriptor.IsServerStreaming)
        {
            using var duplex = invoker.AsyncDuplexStreamingCall(grpcMethod, null, options);
            foreach (var request in requests)
            {
                await duplex.RequestStream.WriteAsync(request, ct).ConfigureAwait(false);
            }

            await duplex.RequestStream.CompleteAsync().ConfigureAwait(false);
            await DrainAsync(duplex.ResponseStream, descriptor.OutputType, ctx, ct).ConfigureAwait(false);
            return ViceExitCode.SUCCESS;
        }

        if (descriptor.IsClientStreaming)
        {
            using var clientCall = invoker.AsyncClientStreamingCall(grpcMethod, null, options);
            foreach (var request in requests)
            {
                await clientCall.RequestStream.WriteAsync(request, ct).ConfigureAwait(false);
            }

            await clientCall.RequestStream.CompleteAsync().ConfigureAwait(false);
            var clientResponse = await clientCall.ResponseAsync.ConfigureAwait(false);
            WriteResponse(clientResponse, descriptor.OutputType, ctx);
            return ViceExitCode.SUCCESS;
        }

        if (descriptor.IsServerStreaming)
        {
            using var serverCall = invoker.AsyncServerStreamingCall(grpcMethod, null, options, requests[0]);
            await DrainAsync(serverCall.ResponseStream, descriptor.OutputType, ctx, ct).ConfigureAwait(false);
            return ViceExitCode.SUCCESS;
        }

        using var unary = invoker.AsyncUnaryCall(grpcMethod, null, options, requests[0]);
        var response = await unary.ResponseAsync.ConfigureAwait(false);
        WriteResponse(response, descriptor.OutputType, ctx);
        return ViceExitCode.SUCCESS;
    }

    private static async Task DrainAsync(
        IAsyncStreamReader<byte[]> responseStream,
        MessageDescriptor outputType,
        CommandContext ctx,
        CancellationToken ct)
    {
        while (await responseStream.MoveNext(ct).ConfigureAwait(false))
        {
            WriteResponse(responseStream.Current, outputType, ctx);
        }
    }

    private static void WriteResponse(byte[] message, MessageDescriptor outputType, CommandContext ctx)
    {
        _ = ctx;
        ctx.Console.WriteLine(Sanitize(ProtobufJsonTranscoder.ProtobufToJson(message, outputType)));
    }

    private static MethodType MethodTypeFor(MethodDescriptor descriptor)
    {
        if (descriptor.IsClientStreaming
            && descriptor.IsServerStreaming)
        {
            return MethodType.DuplexStreaming;
        }

        if (descriptor.IsClientStreaming)
        {
            return MethodType.ClientStreaming;
        }

        if (descriptor.IsServerStreaming)
        {
            return MethodType.ServerStreaming;
        }

        return MethodType.Unary;
    }

    private static readonly Marshaller<byte[]> ByteArrayMarshaller =
        Marshallers.Create(static bytes => bytes, static bytes => bytes);

    private static ServiceDescriptor? FindService(IReadOnlyList<FileDescriptor> pool, string fullName)
    {
        foreach (var file in pool)
        {
            foreach (var service in file.Services)
            {
                if (string.Equals(service.FullName, fullName, StringComparison.Ordinal))
                {
                    return service;
                }
            }
        }

        return null;
    }

    private static string DescribeServiceJson(ServiceDescriptor descriptor)
    {
        var methods = descriptor.Methods.Select(m =>
        {
            var name = JsonEncodedText.Encode(m.Name);
            var input = JsonEncodedText.Encode(m.InputType.FullName);
            var output = JsonEncodedText.Encode(m.OutputType.FullName);
            var clientStreaming = m.IsClientStreaming ? "true" : "false";
            var serverStreaming = m.IsServerStreaming ? "true" : "false";
            return $"{{\"name\":\"{name}\",\"inputType\":\"{input}\",\"outputType\":\"{output}\",\"clientStreaming\":{clientStreaming},\"serverStreaming\":{serverStreaming}}}";
        });
        return $"{{\"service\":\"{JsonEncodedText.Encode(descriptor.FullName)}\",\"methods\":[{string.Join(",", methods)}]}}";
    }

    private static async Task<IReadOnlyList<string>> FetchServiceNamesAsync(
        GrpcChannel channel,
        string endpoint,
        int timeoutMs,
        CancellationToken ct)
    {
        var client = new ServerReflection.ServerReflectionClient(channel);
        using var call = client.ServerReflectionInfo(CallOptionsFor(null, timeoutMs, ct));
        await call.RequestStream.WriteAsync(new ServerReflectionRequest
        {
            Host = endpoint,
            ListServices = "*"
        }, ct).ConfigureAwait(false);
        await call.RequestStream.CompleteAsync().ConfigureAwait(false);

        var services = new List<string>();
        while (await call.ResponseStream.MoveNext(ct).ConfigureAwait(false))
        {
            var response = call.ResponseStream.Current;
            if (response.MessageResponseCase == ServerReflectionResponse.MessageResponseOneofCase.ErrorResponse)
            {
                throw new InvalidOperationException(
                    $"reflection error {response.ErrorResponse.ErrorCode}: {response.ErrorResponse.ErrorMessage}");
            }

            if (response.MessageResponseCase == ServerReflectionResponse.MessageResponseOneofCase.ListServicesResponse)
            {
                foreach (var service in response.ListServicesResponse.Service)
                {
                    services.Add(service.Name);
                }
            }
        }

        services.Sort(StringComparer.Ordinal);
        return services;
    }

    private static async Task<IReadOnlyList<FileDescriptor>> ResolveBySymbolAsync(
        GrpcChannel channel,
        string endpoint,
        string symbol,
        int timeoutMs,
        CancellationToken ct)
    {
        var client = new ServerReflection.ServerReflectionClient(channel);
        using var call = client.ServerReflectionInfo(CallOptionsFor(null, timeoutMs, ct));

        var byteStringsByName = new Dictionary<string, ByteString>(StringComparer.Ordinal);
        var protoByName = new Dictionary<string, FileDescriptorProto>(StringComparer.Ordinal);
        var requested = new HashSet<string>(StringComparer.Ordinal);
        var pendingFiles = new Queue<string>();

        await call.RequestStream.WriteAsync(new ServerReflectionRequest
        {
            Host = endpoint,
            FileContainingSymbol = symbol
        }, ct).ConfigureAwait(false);

        var outstanding = 1;
        while (outstanding > 0)
        {
            if (!await call.ResponseStream.MoveNext(ct).ConfigureAwait(false))
            {
                break;
            }

            outstanding--;
            var response = call.ResponseStream.Current;
            if (response.MessageResponseCase == ServerReflectionResponse.MessageResponseOneofCase.ErrorResponse)
            {
                throw new InvalidOperationException(
                    $"reflection error {response.ErrorResponse.ErrorCode}: {response.ErrorResponse.ErrorMessage}");
            }

            if (response.MessageResponseCase != ServerReflectionResponse.MessageResponseOneofCase.FileDescriptorResponse)
            {
                continue;
            }

            foreach (var raw in response.FileDescriptorResponse.FileDescriptorProto)
            {
                var proto = FileDescriptorProto.Parser.ParseFrom(raw);
                if (byteStringsByName.ContainsKey(proto.Name))
                {
                    continue;
                }

                byteStringsByName[proto.Name] = raw;
                protoByName[proto.Name] = proto;
                pendingFiles.Enqueue(proto.Name);
            }

            while (pendingFiles.Count > 0)
            {
                var fileName = pendingFiles.Dequeue();
                foreach (var dependency in protoByName[fileName].Dependency)
                {
                    if (byteStringsByName.ContainsKey(dependency)
                        || !requested.Add(dependency))
                    {
                        continue;
                    }

                    await call.RequestStream.WriteAsync(new ServerReflectionRequest
                    {
                        Host = endpoint,
                        FileByFilename = dependency
                    }, ct).ConfigureAwait(false);
                    outstanding++;
                }
            }
        }

        await call.RequestStream.CompleteAsync().ConfigureAwait(false);
        return FileDescriptor.BuildFromByteStrings(byteStringsByName.Values);
    }

    private static string Sanitize(string value)
    {
        var clean = true;
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                clean = false;
                break;
            }
        }

        if (clean)
        {
            return value;
        }

        var buffer = new List<char>(value.Length);
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                buffer.Add('�');
            }
            else
            {
                buffer.Add(c);
            }
        }

        return new string(buffer.ToArray());
    }

    private static int Interrupted(CommandContext ctx, string endpoint)
    {
        ctx.Logger.Log(ViceLogLevel.Trace, $"gRPC reflection on {endpoint} cancelled");
        return ViceExitCode.INTERRUPTED;
    }

    private static int Fail(CommandContext ctx, string endpoint, Exception ex)
    {
        switch (ex)
        {
            case ViceError viceError:
                {
                    ctx.Logger.Log(viceError.LogLevel, $"gRPC reflection on {endpoint} failed", viceError);
                    ctx.Console.WriteError(ex.Message);
                    return viceError.ExitCode;
                }
            case ArgumentException argument:
                {
                    ctx.Logger.Log(ViceLogLevel.Warn, $"gRPC reflection on {endpoint} failed", argument);
                    ctx.Console.WriteError(argument.Message);
                    return ViceExitCode.USAGE_ERROR;
                }
            case RpcException rpc:
                {
                    ctx.Logger.Log(ViceLogLevel.Warn, $"gRPC call to {endpoint} faulted", rpc);
                    ctx.Console.WriteError($"gRPC error ({rpc.StatusCode}): {rpc.Status.Detail}");
                    return ViceExitCode.FAILURE;
                }
            default:
                {
                    ctx.Logger.Log(ViceLogLevel.Error, $"gRPC operation on {endpoint} faulted", ex);
                    ctx.Console.WriteError(ex.Message);
                    return ViceExitCode.FAILURE;
                }
        }
    }
}
