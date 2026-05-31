namespace Vice.Net.Commands.Network;

internal static class NetworkConstants
{
    public const int DefaultTimeoutMs = 30_000;
    public const int DefaultUdpTimeoutMs = 5_000;
    public const int DefaultResearchTimeoutMs = 30_000;
    public const int MaxGrpcReceiveBytes = 32 * 1024 * 1024;
    public const int MaxGrpcSendBytes = 32 * 1024 * 1024;
    public const int MaxTcpResponseBytes = 16 * 1024 * 1024;
    public const int MaxJsonBytes = 16 * 1024 * 1024;
}
