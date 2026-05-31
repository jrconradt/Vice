using System.Net;
using System.Net.Sockets;
using System.Text;
using Vice.Mux.Sinks;
using Xunit;

namespace Vice.Mux.Tests;

public class SinkLoopbackTests
{
    [Fact]
    public async Task TcpSink_RoundTripsBytesToLoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var payload = Encoding.ASCII.GetBytes("tcp-sink-roundtrip");
        try
        {
            var acceptTask = listener.AcceptTcpClientAsync();
            await using (var sink = SinkFactory.Open($"tcp:127.0.0.1:{port}"))
            {
                using var server = await acceptTask;
                await sink.WriteAsync(payload, CancellationToken.None);
                await sink.FlushAsync(CancellationToken.None);

                var received = new byte[payload.Length];
                var read = 0;
                var stream = server.GetStream();
                while (read < received.Length)
                {
                    var n = await stream.ReadAsync(received.AsMemory(read), CancellationToken.None);
                    if (n == 0)
                    {
                        break;
                    }

                    read += n;
                }

                Assert.Equal(payload.Length, read);
                Assert.Equal(payload, received);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    [UnixOnlyFact]
    public async Task ProcessSink_RoundTripsStdinThroughChildProcess()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"vice-mux-proc-{Guid.NewGuid():N}.bin");
        var payload = Encoding.ASCII.GetBytes("process-sink-roundtrip");
        try
        {
            await using (var sink = SinkFactory.Open($"exec:tee {outPath}"))
            {
                await sink.WriteAsync(payload, CancellationToken.None);
                await sink.FlushAsync(CancellationToken.None);
            }

            var written = await File.ReadAllBytesAsync(outPath);
            Assert.Equal(payload, written);
        }
        finally
        {
            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }
        }
    }
}
