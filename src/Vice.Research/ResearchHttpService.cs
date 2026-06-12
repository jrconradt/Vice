using Vice.Logging;

namespace Vice.Research;

public sealed class ResearchHttpService : IDisposable
{
    private readonly Lazy<HttpClient> _client;

    public ResearchHttpService(IViceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _client = new Lazy<HttpClient>(() => ResearchHttp.Create(logger),
                                       LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public HttpClient Client => _client.Value;

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
