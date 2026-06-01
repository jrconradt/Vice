using System.Net;
using System.Text;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vice.Jobs;
using Vice.Logging;
using Vice.Net.Requests.Grpc;
using Xunit;

namespace Vice.Net.Tests;

public class GrpcStreamJobRunnerRetryTests : IAsyncLifetime, IDisposable
{
    private WebApplication? _app;
    private string _endpoint = string.Empty;
    private readonly string _tempDir;
    private static readonly RetryServiceConfig _config = new();

    public GrpcStreamJobRunnerRetryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "vice-gsj-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(opts =>
        {
            opts.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2);
        });

        builder.Services.AddGrpc();
        builder.Services.AddSingleton(_config);
        builder.Services.AddSingleton<IServiceMethodProvider<RetryStreamingService>, RetryStreamingMethodProvider>();

        _app = builder.Build();
        _app.MapGrpcService<RetryStreamingService>();

        await _app.StartAsync();
        var u = new Uri(_app.Urls.First());
        _endpoint = $"{u.Host}:{u.Port}";
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
    }

    private static IProgress<JobProgress> NoopProgress() => new Progress<JobProgress>(_ => { });

    [Fact]
    public async Task Transient_unavailable_is_retried_and_all_messages_are_recovered()
    {
        _config.Reset();
        _config.MessagesToYield = 6;
        _config.AbortOnAttempt = 1;
        _config.AbortAfterMessages = 3;
        _config.AbortStatus = StatusCode.Unavailable;

        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        conn.Connect(_endpoint, plaintext: true);

        var runner = new GrpcStreamJobRunner(
            conn,
            maxRetries: 5,
            baseRetryBackoff: TimeSpan.FromMilliseconds(10),
            maxRetryBackoff: TimeSpan.FromMilliseconds(50));
        var dest = Path.Combine(_tempDir, "retry-success.jsonl");

        var job = new JobState
        {
            Id = 100,
            Kind = JobKind.GrpcStream,
            Endpoint = _endpoint,
            Method = "vice.test.RetryStreamer/Stream",
            ResourceId = "{}",
            DestinationPath = dest,
        };

        await runner.RunAsync(job, NoopProgress(), CancellationToken.None);

        Assert.True(File.Exists(dest));
        Assert.False(File.Exists(dest + ".partial"));
        var lines = await File.ReadAllLinesAsync(dest);
        Assert.Equal(6, lines.Length);
        var expected = Enumerable.Range(0, 6).Select(i => $"\"msg-{i}\"").ToArray();
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Contains(expected[i], lines[i]);
        }
    }

    [Fact]
    public async Task Recoverable_abort_that_never_clears_preserves_partial_output()
    {
        _config.Reset();
        _config.MessagesToYield = 10;
        _config.AbortOnEveryAttempt = true;
        _config.AbortAfterMessages = 2;
        _config.AbortStatus = StatusCode.Unavailable;

        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        conn.Connect(_endpoint, plaintext: true);

        var runner = new GrpcStreamJobRunner(
            conn,
            maxRetries: 5,
            baseRetryBackoff: TimeSpan.FromMilliseconds(10),
            maxRetryBackoff: TimeSpan.FromMilliseconds(50));
        var dest = Path.Combine(_tempDir, "retry-exhausted.jsonl");

        var job = new JobState
        {
            Id = 101,
            Kind = JobKind.GrpcStream,
            Endpoint = _endpoint,
            Method = "vice.test.RetryStreamer/Stream",
            ResourceId = "{}",
            DestinationPath = dest,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(job, NoopProgress(), CancellationToken.None));
        Assert.Contains("Unavailable", ex.Message);

        Assert.False(File.Exists(dest));
        Assert.True(File.Exists(dest + ".partial"));
        var partialLines = await File.ReadAllLinesAsync(dest + ".partial");
        Assert.Equal(2, partialLines.Length);
    }

    [Fact]
    public async Task Retry_stops_after_configured_max_attempts()
    {
        _config.Reset();
        _config.MessagesToYield = 10;
        _config.AbortOnEveryAttempt = true;
        _config.AbortAfterMessages = 1;
        _config.AbortStatus = StatusCode.Unavailable;

        const int MAX_RETRIES = 3;

        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        conn.Connect(_endpoint, plaintext: true);

        var runner = new GrpcStreamJobRunner(
            conn,
            maxRetries: MAX_RETRIES,
            baseRetryBackoff: TimeSpan.FromMilliseconds(10),
            maxRetryBackoff: TimeSpan.FromMilliseconds(50));
        var dest = Path.Combine(_tempDir, "retry-max-attempts.jsonl");

        var job = new JobState
        {
            Id = 102,
            Kind = JobKind.GrpcStream,
            Endpoint = _endpoint,
            Method = "vice.test.RetryStreamer/Stream",
            ResourceId = "{}",
            DestinationPath = dest,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(job, NoopProgress(), CancellationToken.None));

        Assert.Equal(MAX_RETRIES + 1, _config.Attempts);
    }

    [Fact]
    public async Task Non_retryable_status_fails_immediately_without_retry()
    {
        _config.Reset();
        _config.MessagesToYield = 10;
        _config.AbortOnEveryAttempt = true;
        _config.AbortAfterMessages = 0;
        _config.AbortStatus = StatusCode.PermissionDenied;

        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        conn.Connect(_endpoint, plaintext: true);

        var runner = new GrpcStreamJobRunner(
            conn,
            maxRetries: 5,
            baseRetryBackoff: TimeSpan.FromMilliseconds(10),
            maxRetryBackoff: TimeSpan.FromMilliseconds(50));
        var dest = Path.Combine(_tempDir, "retry-nonretryable.jsonl");

        var job = new JobState
        {
            Id = 103,
            Kind = JobKind.GrpcStream,
            Endpoint = _endpoint,
            Method = "vice.test.RetryStreamer/Stream",
            ResourceId = "{}",
            DestinationPath = dest,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(job, NoopProgress(), CancellationToken.None));
        Assert.Contains("PermissionDenied", ex.Message);

        Assert.Equal(1, _config.Attempts);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".partial"));
    }

    [Fact]
    public async Task Transient_deadline_exceeded_is_retried_and_recovers()
    {
        _config.Reset();
        _config.MessagesToYield = 6;
        _config.AbortOnAttempt = 1;
        _config.AbortAfterMessages = 3;
        _config.AbortStatus = StatusCode.DeadlineExceeded;

        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        conn.Connect(_endpoint, plaintext: true);

        var runner = new GrpcStreamJobRunner(
            conn,
            maxRetries: 5,
            baseRetryBackoff: TimeSpan.FromMilliseconds(10),
            maxRetryBackoff: TimeSpan.FromMilliseconds(50));
        var dest = Path.Combine(_tempDir, "retry-deadline.jsonl");

        var job = new JobState
        {
            Id = 104,
            Kind = JobKind.GrpcStream,
            Endpoint = _endpoint,
            Method = "vice.test.RetryStreamer/Stream",
            ResourceId = "{}",
            DestinationPath = dest,
        };

        await runner.RunAsync(job, NoopProgress(), CancellationToken.None);

        Assert.Equal(2, _config.Attempts);
        Assert.True(File.Exists(dest));
        Assert.False(File.Exists(dest + ".partial"));
        var lines = await File.ReadAllLinesAsync(dest);
        Assert.Equal(6, lines.Length);
    }

    internal sealed class RetryServiceConfig
    {
        public int MessagesToYield { get; set; } = 1;
        public int AbortAfterMessages { get; set; }
        public int? AbortOnAttempt { get; set; }
        public bool AbortOnEveryAttempt { get; set; }
        public StatusCode AbortStatus { get; set; } = StatusCode.Unavailable;
        private int _attempt;

        public int Attempts => Volatile.Read(ref _attempt);

        public int NextAttempt() => Interlocked.Increment(ref _attempt);

        public void Reset()
        {
            MessagesToYield = 1;
            AbortAfterMessages = 0;
            AbortOnAttempt = null;
            AbortOnEveryAttempt = false;
            AbortStatus = StatusCode.Unavailable;
            Interlocked.Exchange(ref _attempt, 0);
        }
    }

    internal sealed class RetryStreamingService
    {
        private readonly RetryServiceConfig _config;

        public RetryStreamingService(RetryServiceConfig config)
        {
            _config = config;
        }

        public async Task Stream(byte[] request, IServerStreamWriter<byte[]> responseStream, ServerCallContext context)
        {
            var attempt = _config.NextAttempt();
            var abortThisAttempt = _config.AbortOnEveryAttempt
                || (_config.AbortOnAttempt.HasValue && attempt == _config.AbortOnAttempt.Value);

            for (var i = 0; i < _config.MessagesToYield; i++)
            {
                if (abortThisAttempt && i >= _config.AbortAfterMessages)
                {
                    throw new RpcException(new global::Grpc.Core.Status(_config.AbortStatus, "simulated transient abort"));
                }

                var payload = Encoding.UTF8.GetBytes($"{{\"msg-{i}\":\"value\"}}");
                await responseStream.WriteAsync(payload, context.CancellationToken);
            }
        }
    }

    internal sealed class RetryStreamingMethodProvider : IServiceMethodProvider<RetryStreamingService>
    {
        private static readonly Marshaller<byte[]> ByteMarshaller =
            Marshallers.Create(b => b, b => b);

        public void OnServiceMethodDiscovery(ServiceMethodProviderContext<RetryStreamingService> context)
        {
            var method = new Method<byte[], byte[]>(
                MethodType.ServerStreaming,
                "vice.test.RetryStreamer",
                "Stream",
                ByteMarshaller,
                ByteMarshaller);

            context.AddServerStreamingMethod(
                method,
                new List<object>(),
                (RetryStreamingService svc, byte[] req, IServerStreamWriter<byte[]> writer, ServerCallContext ctx) =>
                    svc.Stream(req, writer, ctx));
        }
    }
}
