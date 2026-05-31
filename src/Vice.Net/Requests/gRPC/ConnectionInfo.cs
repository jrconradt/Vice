namespace Vice.Network.gRPC;

public record ConnectionInfo(string Endpoint, DateTime ConnectedAt, int CallCount);
