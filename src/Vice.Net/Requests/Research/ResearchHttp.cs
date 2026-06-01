using System.Net.Http.Headers;
using Vice;
using Vice.Logging;
using Vice.Net.Http;
using Vice.Net.Requests.Grpc;

namespace Vice.Net.Research;

internal static class ResearchHttp
{
    public const string UserAgentEnvVar = "VICE_USER_AGENT";

    public const string ContactEmailEnvVar = "VICE_CONTACT_EMAIL";

    private const string ProjectUrl = "https://lab.freya.cintile.io/atelier/vice";

    private const long MaxResponseContentBufferBytes = 8L * 1024 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(100);

    public static HttpClient Create()
    {
        var inner = new SocketsHttpHandler
        {
            ConnectCallback = SafeOutboundConnection.ConnectAsync,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };

        var polite = new PoliteHandler(inner,
                                       minInterval: TimeSpan.FromSeconds(1),
                                       maxRetries: 3,
                                       logger: new StaticLogForwarder());

        var client = new HttpClient(polite, disposeHandler: true)
        {
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = MaxResponseContentBufferBytes,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ResolveUserAgent());
        return client;
    }

    internal static string ResolveUserAgent()
    {
        var configured = Environment.GetEnvironmentVariable(UserAgentEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return BuildDefaultUserAgent();
    }

    private static string BuildDefaultUserAgent()
    {
        var version = typeof(ResearchHttp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var contact = Environment.GetEnvironmentVariable(ContactEmailEnvVar);
        if (!string.IsNullOrWhiteSpace(contact))
        {
            return $"Vice/{version} (+{ProjectUrl}; mailto:{contact.Trim()})";
        }

        return $"Vice/{version} (+{ProjectUrl})";
    }

    private sealed class StaticLogForwarder : IViceLogger
    {
        public bool IsEnabled(ViceLogLevel level)
        {
            return Vice.Log.IsEnabled(level);
        }

        public void Log(ViceError error)
        {
            Vice.Log.Emit(error);
        }

        public void Log(ViceLogLevel level,
                        string message,
                        Exception? exception = null,
                        string? caller = null,
                        string? file = null,
                        int line = 0)
        {
            Vice.Log.Emit(level,
                          message,
                          exception,
                          caller,
                          file,
                          line);
        }
    }
}
