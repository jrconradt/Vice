using System.Net.Sockets;
using System.Text;

namespace Vice.Host.Core;

internal static class SystemdNotify
{
    public static void Ready()
    {
        Send("READY=1");
    }

    public static void Watchdog()
    {
        Send("WATCHDOG=1");
    }

    public static TimeSpan? WatchdogInterval()
    {
        var usecText = Environment.GetEnvironmentVariable("WATCHDOG_USEC");
        if (string.IsNullOrEmpty(usecText)
            || !long.TryParse(usecText, out var usec)
            || usec <= 0)
        {
            return null;
        }

        var pidText = Environment.GetEnvironmentVariable("WATCHDOG_PID");
        if (!string.IsNullOrEmpty(pidText)
            && (!int.TryParse(pidText, out var pid)
                || pid != Environment.ProcessId))
        {
            return null;
        }

        return TimeSpan.FromMilliseconds(usec / 1000.0 / 2.0);
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
