using System.Runtime.CompilerServices;
using Vice.Network.gRPC;

namespace Vice.Net.Tests.Helpers;

internal static class TestSetup
{
    [ModuleInitializer]
    public static void AllowLoopbackForTests()
    {
        Environment.SetEnvironmentVariable(SafeNetPolicy.AllowIpsEnvVar, "127.0.0.0/8,::1");
        Environment.SetEnvironmentVariable(SafeNetPolicy.AllowHostsEnvVar, "localhost");
        SafeOutboundConnection.ResetPolicy();
    }
}
