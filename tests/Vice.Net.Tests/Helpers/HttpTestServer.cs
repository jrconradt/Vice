using System.Net;
using System.Net.Sockets;

namespace Vice.Net.Tests;

internal sealed class HttpTestServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly Task _acceptLoop;
    private readonly CancellationTokenSource _cts = new();
    public string BaseUrl { get; }

    public HttpTestServer(Func<HttpListenerContext, Task> handler)
    {
        (_listener, BaseUrl) = BindListener();

        _acceptLoop = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    System.Diagnostics.Trace.WriteLine(ex);
                    return;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await handler(ctx);
                    }
                    catch (Exception handlerEx)
                    {
                        System.Diagnostics.Trace.WriteLine(handlerEx);
                        try
                        {
                            ctx.Response.StatusCode = 500; ctx.Response.Close();
                        }
                        catch (Exception closeEx) when (closeEx is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                        {
                            System.Diagnostics.Trace.WriteLine(closeEx);
                        }
                    }
                });
            }
        });
    }

    private static (HttpListener Listener, string BaseUrl) BindListener()
    {
        const int MAX_ATTEMPTS = 32;
        for (var attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            var baseUrl = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(baseUrl);
            try
            {
                probe.Stop();
                listener.Start();
                return (listener, baseUrl);
            }
            catch (HttpListenerException)
            {
                ((IDisposable)listener).Dispose();
            }
            finally
            {
                probe.Stop();
            }
        }

        throw new InvalidOperationException(
            $"could not bind an HttpListener to a free loopback port after {MAX_ATTEMPTS} attempts.");
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
        try
        {
            await _acceptLoop;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
        ((IDisposable)_listener).Dispose();
    }
}
