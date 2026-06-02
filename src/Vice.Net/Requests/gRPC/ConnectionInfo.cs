namespace Vice.Net.Requests.Grpc;

public record ConnectionInfo(string Endpoint, DateTime ConnectedAt, int CallCount);
