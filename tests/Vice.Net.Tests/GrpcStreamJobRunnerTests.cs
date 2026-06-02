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

public class GrpcStreamJobRunnerTests : IAsyncLifetime, IDisposable
{
    private WebApplication? _app;
    private string _endpoint = string.Empty;
    private readonly string _tempDir;
    private static readonly ServiceConfig _config = new();

    public GrpcStreamJobRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "vice-gsj-" + Guid.NewGuid().ToString("N"));
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
        builder.Services.AddSingleton<IServiceMethodProvider<StreamingService>, StreamingMethodProvider>();

        _app = builder.Build();
        _app.MapGrpcService<StreamingService>();

        await _app.StartAsync();
        var addr = _app.Urls.First();
        var u = new Uri(addr);
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
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
    }

    private static IProgress<JobProgress> NoopProgress() => new Progress<JobProgress>(_ => { });

    [Fact]
    public async Task Happy_path_streams_messages_and_writes_to_destination()
    {
        _config.Reset();
        _config.MessagesToYield = 3;

        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        conn.Connect(_endpoint, plaintext: true);

        var runner = new GrpcStreamJobRunner(conn);
        var dest = Path.Combine(_tempDir, "stream.jsonl");

        var job = new JobState
        {
            Id = 1,
            Kind = JobKind.GrpcStream,
            Endpoint = _endpoint,
            Method = "vice.test.Streamer/Stream",
            ResourceId = "{\"hello\":\"world\"}",
            DestinationPath = dest,
        };

        await runner.RunAsync(job, NoopProgress(), CancellationToken.None);

        Assert.True(File.Exists(dest));
        var lines = await File.ReadAllLinesAsync(dest);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l => Assert.Contains("msg-", l));
    }

    [Fact]
    public async Task Server_aborts_with_rpc_exception_is_surfaced_as_InvalidOperationException()
    {
        _config.Reset();
        _config.AbortAfterMessages = 2;
        _config.MessagesToYield = 10;

        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        conn.Connect(_endpoint, plaintext: true);

        var runner = new GrpcStreamJobRunner(conn);
        var dest = Path.Combine(_tempDir, "abort.jsonl");

        var job = new JobState
        {
            Id = 2,
            Kind = JobKind.GrpcStream,
            Endpoint = _endpoint,
            Method = "vice.test.Streamer/Stream",
            ResourceId = "{}",
            DestinationPath = dest,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(job, NoopProgress(), CancellationToken.None));
        Assert.Contains("gRPC error", ex.Message);
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public async Task Cancellation_propagates_OperationCanceledException()
    {
        _config.Reset();
        _config.MessagesToYield = 100;
        _config.DelayMsBetweenMessages = 200;

        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        conn.Connect(_endpoint, plaintext: true);

        var runner = new GrpcStreamJobRunner(conn);
        var dest = Path.Combine(_tempDir, "cancel.jsonl");

        var job = new JobState
        {
            Id = 3,
            Kind = JobKind.GrpcStream,
            Endpoint = _endpoint,
            Method = "vice.test.Streamer/Stream",
            ResourceId = "{}",
            DestinationPath = dest,
        };

        using var cts = new CancellationTokenSource();
        var task = runner.RunAsync(job, NoopProgress(), cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => task);
        Assert.True(
            ex is OperationCanceledException || ex is InvalidOperationException,
            $"Expected cancellation surfaced as OperationCanceledException or InvalidOperationException, got {ex.GetType().Name}: {ex.Message}");
    }

    [Fact]
    public async Task Invalid_method_format_throws_InvalidOperationException()
    {
        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        conn.Connect(_endpoint, plaintext: true);

        var runner = new GrpcStreamJobRunner(conn);
        var job = new JobState
        {
            Id = 4,
            Kind = JobKind.GrpcStream,
            Endpoint = _endpoint,
            Method = "no-slash-here",
            ResourceId = "{}",
            DestinationPath = Path.Combine(_tempDir, "noslash.jsonl"),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(job, NoopProgress(), CancellationToken.None));
        Assert.Contains("Invalid method format", ex.Message);
    }

    [Fact]
    public async Task Nonexistent_method_surfaces_as_InvalidOperationException()
    {
        _config.Reset();

        await using var conn = new GrpcConnectionManager(NullViceLogger.Instance);
        conn.Connect(_endpoint, plaintext: true);

        var runner = new GrpcStreamJobRunner(conn);
        var job = new JobState
        {
            Id = 5,
            Kind = JobKind.GrpcStream,
            Endpoint = _endpoint,
            Method = "vice.test.Streamer/DoesNotExist",
            ResourceId = "{}",
            DestinationPath = Path.Combine(_tempDir, "missing.jsonl"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(job, NoopProgress(), CancellationToken.None));
    }

    [Fact]
    public void CanHandle_returns_true_only_for_GrpcStream()
    {
        var runner = new GrpcStreamJobRunner(new GrpcConnectionManager(NullViceLogger.Instance));
        Assert.True(runner.CanHandle(JobKind.GrpcStream));
        Assert.False(runner.CanHandle(JobKind.Download));
    }

    internal sealed class ServiceConfig
    {
        public int MessagesToYield { get; set; } = 1;
        public int? AbortAfterMessages { get; set; }
        public int DelayMsBetweenMessages { get; set; }

        public void Reset()
        {
            MessagesToYield = 1;
            AbortAfterMessages = null;
            DelayMsBetweenMessages = 0;
        }
    }

    internal sealed class StreamingService
    {
        private readonly ServiceConfig _config;

        public StreamingService(ServiceConfig config)
        {
            _config = config;
        }

        public async Task Stream(byte[] request, IServerStreamWriter<byte[]> responseStream, ServerCallContext context)
        {
            for (var i = 0; i < _config.MessagesToYield; i++)
            {
                if (_config.AbortAfterMessages.HasValue && i >= _config.AbortAfterMessages.Value)
                {
                    throw new RpcException(new global::Grpc.Core.Status(StatusCode.Internal, "simulated abort"));
                }

                var payload = Encoding.UTF8.GetBytes($"{{\"msg-{i}\":\"value\"}}");
                await responseStream.WriteAsync(payload, context.CancellationToken);

                if (_config.DelayMsBetweenMessages > 0)
                {
                    await Task.Delay(_config.DelayMsBetweenMessages, context.CancellationToken);
                }
            }
        }
    }

    internal sealed class StreamingMethodProvider : IServiceMethodProvider<StreamingService>
    {
        private static readonly Marshaller<byte[]> ByteMarshaller =
            Marshallers.Create(b => b, b => b);

        public void OnServiceMethodDiscovery(ServiceMethodProviderContext<StreamingService> context)
        {
            var method = new Method<byte[], byte[]>(
                MethodType.ServerStreaming,
                "vice.test.Streamer",
                "Stream",
                ByteMarshaller,
                ByteMarshaller);

            context.AddServerStreamingMethod(
                method,
                new List<object>(),
                (StreamingService svc, byte[] req, IServerStreamWriter<byte[]> writer, ServerCallContext ctx) =>
                    svc.Stream(req, writer, ctx));
        }
    }
}
