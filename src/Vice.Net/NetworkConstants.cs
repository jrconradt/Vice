namespace Vice.Net.Commands.Network;

internal static class NetworkConstants
{
    public const int DEFAULT_TIMEOUT_MS = 30_000;
    public const int DEFAULT_UDP_TIMEOUT_MS = 5_000;
    public const int DEFAULT_RESEARCH_TIMEOUT_MS = 30_000;
    public const int MAX_GRPC_RECEIVE_BYTES = 32 * 1024 * 1024;
    public const int MAX_GRPC_SEND_BYTES = 32 * 1024 * 1024;
    public const int MAX_TCP_RESPONSE_BYTES = 16 * 1024 * 1024;
}
