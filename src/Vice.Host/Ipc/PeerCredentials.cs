using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Vice.Ipc;

internal static class PeerCredentials
{
    private const int SOL_SOCKET = 1;
    private const int SO_PEERCRED = 17;
    private const int SOL_LOCAL = 0;
    private const int LOCAL_PEERPID = 0x002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Ucred
    {
        public int Pid;
        public int Uid;
        public int Gid;
    }

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int LinuxGetSockOpt(int sockfd, int level, int optname, ref Ucred optval, ref int optlen);

    [DllImport("libc", EntryPoint = "getpeereid", SetLastError = true)]
    private static extern int MacGetPeerEid(int sockfd, ref uint euid, ref uint egid);

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int MacGetSockOpt(int sockfd,
                                            int level,
                                            int optname,
                                            ref int optval,
                                            ref int optlen);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint LibcGeteuid();

    public static bool TryGetPeerUid(SafeHandle handle, out int peerUid)
    {
        peerUid = -1;
        if (handle is null || handle.IsInvalid
            || handle.IsClosed)
        {
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return TryGetPeerUidLinux(handle, out peerUid);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return TryGetPeerUidMac(handle, out peerUid);
        }

        return false;
    }

    public static bool TryGetPeerCredentials(SafeHandle handle, out int peerUid, out int peerPid)
    {
        peerUid = -1;
        peerPid = -1;
        if (handle is null || handle.IsInvalid
            || handle.IsClosed)
        {
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return TryGetPeerCredentialsLinux(handle, out peerUid, out peerPid);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return TryGetPeerCredentialsMac(handle, out peerUid, out peerPid);
        }

        return false;
    }

    public static bool TryGetEuid(out int euid)
    {
        try
        {
            euid = unchecked((int)LibcGeteuid());
            return true;
        }
        catch (Exception)
        {
            euid = -1;
            return false;
        }
    }

    [SupportedOSPlatform("linux")]
    private static bool TryGetPeerUidLinux(SafeHandle handle, out int peerUid)
    {
        peerUid = -1;
        var fd = handle.DangerousGetHandle().ToInt32();
        if (fd < 0)
        {
            return false;
        }

        var cred = default(Ucred);
        var len = Marshal.SizeOf<Ucred>();
        var rc = LinuxGetSockOpt(fd, SOL_SOCKET, SO_PEERCRED, ref cred, ref len);
        if (rc != 0)
        {
            return false;
        }

        peerUid = cred.Uid;
        return true;
    }

    [SupportedOSPlatform("macos")]
    private static bool TryGetPeerUidMac(SafeHandle handle, out int peerUid)
    {
        peerUid = -1;
        var fd = handle.DangerousGetHandle().ToInt32();
        if (fd < 0)
        {
            return false;
        }

        uint euid = 0;
        uint egid = 0;
        var rc = MacGetPeerEid(fd, ref euid, ref egid);
        if (rc != 0)
        {
            return false;
        }

        peerUid = unchecked((int)euid);
        return true;
    }

    [SupportedOSPlatform("macos")]
    private static bool TryGetPeerCredentialsMac(SafeHandle handle, out int peerUid, out int peerPid)
    {
        peerUid = -1;
        peerPid = -1;
        var fd = handle.DangerousGetHandle().ToInt32();
        if (fd < 0)
        {
            return false;
        }

        uint euid = 0;
        uint egid = 0;
        var eidRc = MacGetPeerEid(fd, ref euid, ref egid);
        if (eidRc != 0)
        {
            return false;
        }

        peerUid = unchecked((int)euid);

        var pid = 0;
        var len = Marshal.SizeOf<int>();
        var pidRc = MacGetSockOpt(fd, SOL_LOCAL, LOCAL_PEERPID, ref pid, ref len);
        if (pidRc == 0)
        {
            peerPid = pid;
        }

        return true;
    }

    [SupportedOSPlatform("linux")]
    private static bool TryGetPeerCredentialsLinux(SafeHandle handle, out int peerUid, out int peerPid)
    {
        peerUid = -1;
        peerPid = -1;
        var fd = handle.DangerousGetHandle().ToInt32();
        if (fd < 0)
        {
            return false;
        }

        var cred = default(Ucred);
        var len = Marshal.SizeOf<Ucred>();
        var rc = LinuxGetSockOpt(fd, SOL_SOCKET, SO_PEERCRED, ref cred, ref len);
        if (rc != 0)
        {
            return false;
        }

        peerUid = cred.Uid;
        peerPid = cred.Pid;
        return true;
    }
}
