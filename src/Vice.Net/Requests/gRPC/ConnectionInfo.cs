namespace Vice.Network.gRPC;

internal record ConnectionInfo(string Endpoint, DateTime ConnectedAt, int CallCount);
