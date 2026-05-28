using System.Net;
using System.Net.Sockets;

namespace Vice.Net.Tests;

internal sealed class TcpEchoServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public int Port { get; }

    public TcpEchoServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _acceptLoop = Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using (client)
                            {
                                var stream = client.GetStream();
                                var buf = new byte[4096];
                                int n;
                                while ((n = await stream.ReadAsync(buf, _cts.Token)) > 0)
                                {
                                    await stream.WriteAsync(buf.AsMemory(0, n), _cts.Token);
                                }
                            }
                        }
                        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                        {
                            System.Diagnostics.Trace.WriteLine(ex);
                        }
                    });
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                System.Diagnostics.Trace.WriteLine(ex);
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
        try
        {
            await _acceptLoop;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
        _cts.Dispose();
    }
}
