using System.Diagnostics;
using System.Text;
using Grpc.Core;
using Vice.Jobs;
using Vice.Net.Http;
using Vice.Persistence;
namespace Vice.Network.gRPC;

internal sealed class GrpcStreamJobRunner : IJobRunner
{
    internal const int DefaultMaxRetries = 5;
    internal static readonly TimeSpan DefaultBaseRetryBackoff = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan DefaultMaxRetryBackoff = TimeSpan.FromSeconds(30);

    private readonly GrpcConnectionManager _connectionManager;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseRetryBackoff;
    private readonly TimeSpan _maxRetryBackoff;

    public GrpcStreamJobRunner(GrpcConnectionManager connectionManager)
        : this(connectionManager,
               DefaultMaxRetries,
               DefaultBaseRetryBackoff,
               DefaultMaxRetryBackoff)
    {
    }

    internal GrpcStreamJobRunner(GrpcConnectionManager connectionManager,
                                 int maxRetries,
                                 TimeSpan baseRetryBackoff,
                                 TimeSpan maxRetryBackoff)
    {
        _connectionManager = connectionManager;
        _maxRetries = maxRetries;
        _baseRetryBackoff = baseRetryBackoff;
        _maxRetryBackoff = maxRetryBackoff;
    }

    public bool CanHandle(JobKind kind) => kind == JobKind.GrpcStream;

    public async Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
    {
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
        var label = $"Streaming {job.Method} -> {outputPath}";

        long messagesReceived = 0;
        var attempt = 0;
        while (true)
        {
            var append = attempt > 0;
            try
            {
                messagesReceived = await StreamOnceAsync(
                    job,
                    grpcMethod,
                    requestBytes,
                    partialPath,
                    label,
                    append,
                    messagesReceived,
                    progress,
                    ct).ConfigureAwait(false);

                progress.Report(new JobProgress(
                    MessagesReceived: messagesReceived,
                    Label: label));

                File.Move(partialPath, outputPath, overwrite: true);
                return;
            }
            catch (OperationCanceledException)
            {
                TryDeletePartial(partialPath);
                throw;
            }
            catch (RpcException ex)
            {
                if (IsRecoverable(ex.StatusCode)
                    && attempt < _maxRetries
                    && !ct.IsCancellationRequested)
                {
                    attempt++;
                    var backoff = ComputeBackoff(attempt);
                    progress.Report(new JobProgress(
                        MessagesReceived: messagesReceived,
                        Label: $"{label} (retry {attempt}/{_maxRetries} after {ex.StatusCode})"));

                    try
                    {
                        await Task.Delay(backoff, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        TryDeletePartial(partialPath);
                        throw;
                    }

                    continue;
                }

                if (!IsRecoverable(ex.StatusCode))
                {
                    TryDeletePartial(partialPath);
                }

                throw new InvalidOperationException(
                    $"gRPC error ({ex.StatusCode}): {ex.Status.Detail}", ex);
            }
            catch
            {
                TryDeletePartial(partialPath);
                throw;
            }
        }
    }

    private async Task<long> StreamOnceAsync(
        JobState job,
        Method<byte[], byte[]> grpcMethod,
        byte[] requestBytes,
        string partialPath,
        string label,
        bool append,
        long alreadyWritten,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        var channel = _connectionManager.GetChannel(job.Endpoint!);
        var invoker = channel.CreateCallInvoker();

        var callOptions = new CallOptions(cancellationToken: ct);

        using var streamingCall = invoker.AsyncServerStreamingCall(
            grpcMethod, null, callOptions, requestBytes);

        var writer = new StreamWriter(partialPath, append: append, Encoding.UTF8);
        try
        {
            var messagesReceived = alreadyWritten;
            var skipRemaining = alreadyWritten;
            var lastReport = Stopwatch.GetTimestamp();
            while (await streamingCall.ResponseStream.MoveNext(ct).ConfigureAwait(false))
            {
                if (skipRemaining > 0)
                {
                    skipRemaining--;
                    continue;
                }

                var responseBytes = streamingCall.ResponseStream.Current;
                var responseLine = Encoding.UTF8.GetString(responseBytes);

                await writer.WriteLineAsync(responseLine).ConfigureAwait(false);

                messagesReceived++;

                var elapsed = Stopwatch.GetElapsedTime(lastReport);
                if (elapsed >= HttpStreamHelper.ProgressThrottle)
                {
                    progress.Report(new JobProgress(
                        MessagesReceived: messagesReceived,
                        Label: label));
                    lastReport = Stopwatch.GetTimestamp();
                }
            }

            return messagesReceived;
        }
        finally
        {
            await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            await writer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        var scaled = _baseRetryBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(scaled, _maxRetryBackoff.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped);
    }

    private static bool IsRecoverable(StatusCode status)
    {
        return status == StatusCode.Unavailable
            || status == StatusCode.DeadlineExceeded;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
    }
}
