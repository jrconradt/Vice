using System.Net;
using Vice.Logging;
using Vice.Net.Requests.Grpc;

namespace Vice.Net.Requests.Http;

public static class SafeHttpClient
{
    private static readonly TimeSpan DefaultMinInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(100);

    private const int DEFAULT_MAX_RETRIES = 3;
    private const long DEFAULT_MAX_RESPONSE_BUFFER_BYTES = 8L * 1024 * 1024;

    public static HttpClient Create(IViceLogger logger,
                                    string userAgent,
                                    TimeSpan? requestTimeout = null,
                                    long? maxResponseContentBufferBytes = null,
                                    TimeSpan? minInterval = null,
                                    int maxRetries = DEFAULT_MAX_RETRIES,
                                    IReadOnlyDictionary<string, TimeSpan>? hostMinIntervals = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);

        var inner = new SocketsHttpHandler
        {
            ConnectCallback = (context, ct) => SafeOutboundConnection.ConnectAsync(context, ct, logger),
            AutomaticDecompression = DecompressionMethods.All,
        };

        var polite = new PoliteHandler(inner,
                                       minInterval: minInterval ?? DefaultMinInterval,
                                       maxRetries: maxRetries,
                                       logger: logger,
                                       hostMinIntervals: hostMinIntervals);

        var client = new HttpClient(polite, disposeHandler: true)
        {
            Timeout = requestTimeout ?? DefaultRequestTimeout,
            MaxResponseContentBufferSize = maxResponseContentBufferBytes ?? DEFAULT_MAX_RESPONSE_BUFFER_BYTES,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }
}
