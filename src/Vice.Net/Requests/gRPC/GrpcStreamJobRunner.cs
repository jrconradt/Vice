using System.Diagnostics;
using System.Text;
using Grpc.Core;
using Vice.Jobs;
using Vice.Net.Http;
using Vice.Persistence;
namespace Vice.Net.Requests.Grpc;

internal sealed class GrpcStreamJobRunner : IJobRunner
{
    internal const int DefaultMaxRetries = 5;
    internal static readonly TimeSpan DefaultBaseRetryBackoff = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan DefaultMaxRetryBackoff = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan DefaultCallDeadline = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(2);

    private readonly GrpcConnectionManager _connectionManager;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseRetryBackoff;
    private readonly TimeSpan _maxRetryBackoff;
    private readonly TimeSpan _callDeadline;
    private readonly TimeSpan _idleTimeout;

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
        : this(connectionManager,
               maxRetries,
               baseRetryBackoff,
               maxRetryBackoff,
               DefaultCallDeadline,
               DefaultIdleTimeout)
    {
    }

    internal GrpcStreamJobRunner(GrpcConnectionManager connectionManager,
                                 int maxRetries,
                                 TimeSpan baseRetryBackoff,
                                 TimeSpan maxRetryBackoff,
                                 TimeSpan callDeadline,
                                 TimeSpan idleTimeout)
    {
        _connectionManager = connectionManager;
        _maxRetries = maxRetries;
        _baseRetryBackoff = baseRetryBackoff;
        _maxRetryBackoff = maxRetryBackoff;
        _callDeadline = callDeadline;
        _idleTimeout = idleTimeout;
    }

    public bool CanHandle(JobKind kind) => kind == JobKind.GrpcStream;

    public async Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
    {
        var method = job.Method!;
        if (!GrpcMethodPath.TryParse(method, out var path))
        {
            throw new InvalidOperationException(
                $"Invalid method format: '{method}'. Expected: package.Service/Method");
        }

        var serviceName = path.ServiceName;
        var methodName = path.MethodName;

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
            try
            {
                messagesReceived = await StreamOnceAsync(
                    job,
                    grpcMethod,
                    requestBytes,
                    partialPath,
                    label,
                    written => messagesReceived = written,
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
                SafeFile.TryDelete(partialPath);
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
                        SafeFile.TryDelete(partialPath);
                        throw;
                    }

                    continue;
                }

                if (!IsRecoverable(ex.StatusCode))
                {
                    SafeFile.TryDelete(partialPath);
                }

                throw new InvalidOperationException(
                    $"gRPC error ({ex.StatusCode}): {ex.Status.Detail}", ex);
            }
            catch
            {
                SafeFile.TryDelete(partialPath);
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
        Action<long> onMessageWritten,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        var invoker = _connectionManager.GetChannel(job.Endpoint!).CreateCallInvoker();

        var callOptions = new CallOptions(cancellationToken: ct)
            .WithDeadline(DateTime.UtcNow.Add(_callDeadline));

        using var streamingCall = invoker.AsyncServerStreamingCall(
            grpcMethod, null, callOptions, requestBytes);

        var writer = new StreamWriter(partialPath, append: false, Encoding.UTF8);
        try
        {
            var messagesReceived = 0L;
            onMessageWritten(messagesReceived);
            var lastReport = Stopwatch.GetTimestamp();
            while (await MoveNextWithIdleTimeoutAsync(streamingCall.ResponseStream, ct).ConfigureAwait(false))
            {
                var responseBytes = streamingCall.ResponseStream.Current;
                var responseLine = Encoding.UTF8.GetString(responseBytes);

                await writer.WriteLineAsync(responseLine).ConfigureAwait(false);

                messagesReceived++;
                onMessageWritten(messagesReceived);

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

    private async Task<bool> MoveNextWithIdleTimeoutAsync(
        IAsyncStreamReader<byte[]> responseStream,
        CancellationToken ct)
    {
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idleCts.CancelAfter(_idleTimeout);
        try
        {
            return await responseStream.MoveNext(idleCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && idleCts.IsCancellationRequested)
        {
            throw new RpcException(new global::Grpc.Core.Status(
                StatusCode.DeadlineExceeded,
                $"gRPC stream idle for longer than {_idleTimeout.TotalSeconds:0}s"));
        }
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        var scaled = _baseRetryBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(scaled, _maxRetryBackoff.TotalMilliseconds);
        var jittered = capped * (0.5 + Random.Shared.NextDouble() * 0.5);
        return TimeSpan.FromMilliseconds(jittered);
    }

    private static bool IsRecoverable(StatusCode status)
    {
        return status == StatusCode.Unavailable
            || status == StatusCode.DeadlineExceeded;
    }

}
