using System.Net;
using System.Net.Sockets;

namespace Vice.Net.Tests;

internal sealed class UdpEchoServer : IAsyncDisposable
{
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public int Port { get; }

    public UdpEchoServer()
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

        _loop = Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    UdpReceiveResult r;
                    try
                    {
                        r = await _udp.ReceiveAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        await _udp.SendAsync(r.Buffer, r.Buffer.Length, r.RemoteEndPoint);
                    }
                    catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                    {
                        System.Diagnostics.Trace.WriteLine(ex);
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
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
            _udp.Close();
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
        try
        {
            await _loop;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
        _cts.Dispose();
        _udp.Dispose();
    }
}
