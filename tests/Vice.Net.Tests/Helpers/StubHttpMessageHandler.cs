using System.Net;

namespace Vice.Net.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _asyncResponder;
    public List<Uri> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> asyncResponder)
    {
        _asyncResponder = asyncResponder;
        _responder = _ => throw new InvalidOperationException("Async responder configured; sync path not available.");
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        if (_asyncResponder is not null)
        {
            return await _asyncResponder(request, cancellationToken).ConfigureAwait(false);
        }

        return _responder(request);
    }

    public static HttpResponseMessage Ok(string body, string mediaType = "application/json")
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType)
        };

    public static HttpResponseMessage NotFound()
        => new(HttpStatusCode.NotFound);

    public static HttpResponseMessage WithStatus(HttpStatusCode code, string body = "", string mediaType = "text/plain")
        => new(code)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType)
        };
}
