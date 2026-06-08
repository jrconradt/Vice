using System.Diagnostics;
using System.Text;
using Grpc.Core;
using Vice.Jobs;
using Vice.Logging;
using Vice.Net.Requests.Http;
using Vice.Persistence;
namespace Vice.Net.Requests.Grpc;

internal sealed class GrpcStreamJobRunner : IJobRunner
{
    internal static readonly JobKind StreamKind = JobKind.Custom("GrpcStream");

    internal const string ENDPOINT_KEY = "endpoint";
    internal const string METHOD_KEY = "method";
    internal const string REQUEST_DATA_KEY = "requestData";
    internal const string DESTINATION_PATH_KEY = "destinationPath";

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
    private readonly IViceLogger _logger;

    public GrpcStreamJobRunner(GrpcConnectionManager connectionManager,
                               IViceLogger? logger = null)
        : this(connectionManager,
               DefaultMaxRetries,
               DefaultBaseRetryBackoff,
               DefaultMaxRetryBackoff,
               logger)
    {
    }

    internal GrpcStreamJobRunner(GrpcConnectionManager connectionManager,
                                 int maxRetries,
                                 TimeSpan baseRetryBackoff,
                                 TimeSpan maxRetryBackoff,
                                 IViceLogger? logger = null)
        : this(connectionManager,
               maxRetries,
               baseRetryBackoff,
               maxRetryBackoff,
               DefaultCallDeadline,
               DefaultIdleTimeout,
               logger)
    {
    }

    internal GrpcStreamJobRunner(GrpcConnectionManager connectionManager,
                                 int maxRetries,
                                 TimeSpan baseRetryBackoff,
                                 TimeSpan maxRetryBackoff,
                                 TimeSpan callDeadline,
                                 TimeSpan idleTimeout,
                                 IViceLogger? logger = null)
    {
        _connectionManager = connectionManager;
        _maxRetries = maxRetries;
        _baseRetryBackoff = baseRetryBackoff;
        _maxRetryBackoff = maxRetryBackoff;
        _callDeadline = callDeadline;
        _idleTimeout = idleTimeout;
        _logger = logger ?? NullViceLogger.Instance;
    }

    public bool CanHandle(JobKind kind) => kind == StreamKind;

    public void OnEvicted(JobState job)
    {
        var outputPath = ResolveOutputPath(job);
        SafeFile.TryDelete(outputPath + ".partial");
    }

    private static string ResolveOutputPath(JobState job)
    {
        var configuredDestination = Option(job, DESTINATION_PATH_KEY);
        return !string.IsNullOrWhiteSpace(configuredDestination)
            ? configuredDestination
            : Path.Combine(Path.GetTempPath(), $"vice-grpc-stream-{job.Id}.jsonl");
    }

    public async Task RunAsync(JobState job, IProgress<JobProgress> progress, CancellationToken ct)
    {
        var method = Option(job, METHOD_KEY);
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

        var configuredRequest = Option(job, REQUEST_DATA_KEY);
        var requestData = !string.IsNullOrWhiteSpace(configuredRequest) ? configuredRequest : "{}";
        var requestBytes = Encoding.UTF8.GetBytes(requestData);

        var outputPath = ResolveOutputPath(job);

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
        var label = $"Streaming {method} -> {outputPath}";

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
                    Current: messagesReceived,
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
                    _logger.Log(ViceLogLevel.Warn,
                                $"gRPC stream {method} -> {outputPath} transient failure {ex.StatusCode} (retry {attempt}/{_maxRetries} after {backoff.TotalMilliseconds:0}ms)",
                                ex);
                    progress.Report(new JobProgress(
                        Current: messagesReceived,
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

                _logger.Log(ViceLogLevel.Error,
                            $"gRPC stream {method} -> {outputPath} failed after {attempt} retr{(attempt == 1 ? "y" : "ies")}: {ex.StatusCode} {ex.Status.Detail}",
                            ex);
                throw new InvalidOperationException(
                    $"gRPC error ({ex.StatusCode}): {ex.Status.Detail}", ex);
            }
            catch (Exception ex)
            {
                SafeFile.TryDelete(partialPath);
                _logger.Log(ViceLogLevel.Error,
                            $"gRPC stream {method} -> {outputPath} failed unexpectedly",
                            ex);
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
        var invoker = _connectionManager.GetChannel(Option(job, ENDPOINT_KEY)).CreateCallInvoker();

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
                        Current: messagesReceived,
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

    private static string Option(JobState job, string key)
    {
        return job.Options.TryGetValue(key, out var value) && value is not null
            ? value
            : string.Empty;
    }
}
