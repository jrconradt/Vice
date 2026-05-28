using System.Net;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vice.Net.Tests.Grpc;
using Xunit;

namespace Vice.Net.Tests;

public sealed class GrpcTestServerFixture : IAsyncLifetime
{
    private WebApplication? _app;

    public string ServerUrl { get; private set; } = string.Empty;

    public string Endpoint { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2);
        });
        builder.Services.AddGrpc();
        builder.Services.AddGrpcReflection();

        var app = builder.Build();
        app.MapGrpcService<GreeterService>();
        app.MapGrpcReflectionService();

        await app.StartAsync();

        var address = app.Urls.First();
        ServerUrl = address;
        var uri = new Uri(address);
        Endpoint = $"{uri.Host}:{uri.Port}";
        _app = app;
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class GreeterService : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
            => Task.FromResult(new HelloReply { Message = $"Hello, {request.Name}!" });
    }
}
