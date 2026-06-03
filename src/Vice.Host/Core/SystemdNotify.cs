using System.Net.Sockets;
using System.Text;

namespace Vice.Host.Core;

internal static class SystemdNotify
{
    public static void Ready()
    {
        Send("READY=1");
    }

    private static void Send(string state)
    {
        var socketPath = Environment.GetEnvironmentVariable("NOTIFY_SOCKET");
        if (string.IsNullOrEmpty(socketPath))
        {
            return;
        }

        var endpoint = socketPath[0] == '@'
            ? new UnixDomainSocketEndPoint($"\0{socketPath.Substring(1)}")
            : new UnixDomainSocketEndPoint(socketPath);

        using var socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
        socket.Connect(endpoint);
        socket.Send(Encoding.UTF8.GetBytes(state));
    }
}
