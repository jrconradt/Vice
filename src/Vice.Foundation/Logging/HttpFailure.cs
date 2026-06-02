namespace Vice.Logging;

public sealed class HttpFailure(HttpRequestException inner) : ViceError(inner, inner.Message, (int?)inner.StatusCode)
{
    public HttpRequestException Inner => (HttpRequestException)InnerException!;
    public override ViceLogLevel LogLevel => ViceLogLevel.Warn;
    public override string? Hint => Inner.StatusCode switch
    {
        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            => "Check authentication credentials and access permissions.",
        System.Net.HttpStatusCode.NotFound
            => "Verify the URL or resource ID is correct.",
        System.Net.HttpStatusCode.TooManyRequests
            => "Rate-limited; PoliteHandler will retry. Increase the inter-request interval if this recurs.",
        >= System.Net.HttpStatusCode.InternalServerError and <= (System.Net.HttpStatusCode)599
            => "The server reported a 5xx error; retry later or check upstream status.",
        _ => null,
    };
    public override string ToString() => $"HTTP error: {Inner.Message}";
}
