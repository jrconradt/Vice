using System.Text;
using Grpc.Core;
using Vice.Jobs;
using Vice.Persistence;
namespace Vice.Network.gRPC;

internal sealed class GrpcStreamJobRunner : IJobRunner
{
    private readonly GrpcConnectionManager _connectionManager;

    public GrpcStreamJobRunner(GrpcConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public bool CanHandle(JobKind kind) => kind == JobKind.GrpcStream;

    public async Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
    {
        var channel = _connectionManager.GetChannel(job.Endpoint!);
        var invoker = channel.CreateCallInvoker();

        var method = job.Method!;
        var slashIndex = method.LastIndexOf('/');
        if (slashIndex < 0)
        {
            throw new InvalidOperationException(
                $"Invalid method format: '{method}'. Expected: package.Service/Method");
        }

        var serviceName = method[..slashIndex];
        var methodName = method[(slashIndex + 1)..];

        var requestMarshaller = Marshallers.Create(
            serializer: bytes => bytes,
            deserializer: bytes => bytes);
        var responseMarshaller = Marshallers.Create(
            serializer: bytes => bytes,
            deserializer: bytes => bytes);

        var grpcMethod = new Method<byte[], byte[]>(
            MethodType.ServerStreaming, serviceName, methodName,
            requestMarshaller, responseMarshaller);

        var requestData = !string.IsNullOrWhiteSpace(job.ResourceId) ? job.ResourceId : "{}";
        var requestBytes = Encoding.UTF8.GetBytes(requestData);

        var outputPath = !string.IsNullOrWhiteSpace(job.DestinationPath)
            ? job.DestinationPath
            : Path.Combine(Path.GetTempPath(), $"vice-grpc-stream-{job.Id}.jsonl");

        var fullOutputPath = Path.GetFullPath(outputPath);
        if (!SafeWriteRoots.IsAllowed(fullOutputPath, out var rejectionReason))
        {
            throw new UnauthorizedAccessException(
                $"gRPC stream destination '{fullOutputPath}' is outside allowed roots: {rejectionReason}");
        }

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var partialPath = outputPath + ".partial";

        StreamWriter? writer = null;
        try
        {
            var callOptions = new CallOptions(cancellationToken: ct);

            using var streamingCall = invoker.AsyncServerStreamingCall(
                grpcMethod, null, callOptions, requestBytes);

            writer = new StreamWriter(partialPath, append: false, Encoding.UTF8);

            long messagesReceived = 0;
            try
            {
                while (await streamingCall.ResponseStream.MoveNext(ct))
                {
                    var responseBytes = streamingCall.ResponseStream.Current;
                    var responseLine = Encoding.UTF8.GetString(responseBytes);

                    await writer.WriteLineAsync(responseLine);
                    await writer.FlushAsync(ct);

                    messagesReceived++;

                    progress.Report(new JobProgress(
                        MessagesReceived: messagesReceived,
                        Label: $"Streaming {job.Method} -> {outputPath}"));
                }
            }
            finally
            {
                await writer.FlushAsync(CancellationToken.None);
                await writer.DisposeAsync();
                writer = null;
            }

            File.Move(partialPath, outputPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            if (writer is not null)
            {
                await writer.DisposeAsync();
            }

            TryDeletePartial(partialPath);
            throw;
        }
        catch (RpcException ex)
        {
            if (writer is not null)
            {
                await writer.DisposeAsync();
            }

            TryDeletePartial(partialPath);
            throw new InvalidOperationException(
                $"gRPC error ({ex.StatusCode}): {ex.Status.Detail}", ex);
        }
        catch
        {
            if (writer is not null)
            {
                await writer.DisposeAsync();
            }

            TryDeletePartial(partialPath);
            throw;
        }
    }

    private static void TryDeletePartial(string partialPath)
    {
        try
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
        catch
        {
        }
    }
}
