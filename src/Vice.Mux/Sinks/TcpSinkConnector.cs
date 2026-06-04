using Vice.Logging;

namespace Vice.Mux.Sinks;

public delegate ValueTask<ISink> TcpSinkConnector(string hostPort, CancellationToken ct, IViceLogger logger);
