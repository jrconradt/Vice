using System.Net;
using System.Net.Http.Headers;
using Vice.Net.Requests.Http;
using Xunit;

namespace Vice.Net.Tests;

public class PoliteHandlerHeaderRedactionTests
{
    private sealed class RetryRecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestHeaders> ReceivedHeaders { get; } = new();
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var snapshot = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var (key, values) in request.Headers)
            {
                snapshot.Headers.TryAddWithoutValidation(key, values);
            }

            ReceivedHeaders.Add(snapshot.Headers);

            _calls++;
            var status = _calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    [Fact]
    public async Task Retry_clone_retains_credentials_on_same_origin_retry()
    {
        var inner = new RetryRecordingHandler();
        var polite = new PoliteHandler(inner, minInterval: TimeSpan.Zero, maxRetries: 2);
        using var invoker = new HttpMessageInvoker(polite);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/x");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret");
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "key123");
        request.Headers.TryAddWithoutValidation("X-Auth-Token", "tok456");
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Bearer proxy");
        request.Headers.TryAddWithoutValidation("X-Trace-Id", "abc");

        using var response = await invoker.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.ReceivedHeaders.Count);

        var retry = inner.ReceivedHeaders[1];
        Assert.True(retry.Contains("Authorization"));
        Assert.True(retry.Contains("Cookie"));
        Assert.True(retry.Contains("X-Api-Key"));
        Assert.True(retry.Contains("X-Auth-Token"));
        Assert.True(retry.Contains("Proxy-Authorization"));
        Assert.True(retry.Contains("X-Trace-Id"));
    }
}
