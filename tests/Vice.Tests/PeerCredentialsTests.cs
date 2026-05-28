using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Vice.Ipc;
using Xunit;

namespace Vice.Tests;

public class PeerCredentialsTests
{
    [Fact]
    public void TryGetPeerUid_InvalidHandle_ReturnsFalseGracefully()
    {
        var handle = new InvalidSafeHandle();
        var ok = PeerCredentials.TryGetPeerUid(handle, out var uid);
        Assert.False(ok);
        Assert.Equal(-1, uid);
    }

    [Fact]
    public void TryGetPeerUid_NullHandle_ReturnsFalse()
    {
        var ok = PeerCredentials.TryGetPeerUid(null!, out var uid);
        Assert.False(ok);
        Assert.Equal(-1, uid);
    }

    [Fact]
    public void TryGetEuid_ReturnsNonNegativeValueOnUnix()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
        {
            return;
        }

        var ok = PeerCredentials.TryGetEuid(out var euid);
        Assert.True(ok);
        Assert.True(euid >= 0);
    }

    [Fact]
    public void TryGetPeerUid_OnUnixDomainSocketPair_ReturnsCurrentEuid()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var path = Path.Combine(Path.GetTempPath(), $"vice-peercred-{Guid.NewGuid():N}.sock");
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);

            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(path));
            using var accepted = listener.Accept();

            var fd = (int)accepted.Handle;
            using var handle = new SocketSafeHandle(fd);

            var gotEuid = PeerCredentials.TryGetEuid(out var euid);
            var gotPeer = PeerCredentials.TryGetPeerUid(handle, out var peerUid);

            Assert.True(gotEuid);
            Assert.True(gotPeer);
            Assert.Equal(euid, peerUid);
        }
        finally
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.WriteLine(ex);
            }
        }
    }

    private sealed class InvalidSafeHandle : SafeHandle
    {
        public InvalidSafeHandle() : base(IntPtr.Zero, ownsHandle: false) { }
        public override bool IsInvalid => true;
        protected override bool ReleaseHandle() => true;
    }

    private sealed class SocketSafeHandle : SafeHandle
    {
        public SocketSafeHandle(int fd) : base(IntPtr.Zero, ownsHandle: false)
        {
            SetHandle(new IntPtr(fd));
        }
        public override bool IsInvalid => handle == IntPtr.Zero || handle.ToInt64() < 0;
        protected override bool ReleaseHandle() => true;
    }
}
