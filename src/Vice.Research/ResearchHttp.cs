using Vice.Logging;
using Vice.Net.Requests.Http;

namespace Vice.Research;

internal static class ResearchHttp
{
    public const string UserAgentEnvVar = "VICE_USER_AGENT";

    public const string ContactEmailEnvVar = "VICE_CONTACT_EMAIL";

    private const string ProjectUrl = "https://github.com/jrconradt/Vice";

    private const long MaxResponseContentBufferBytes = 8L * 1024 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(100);

    private static IReadOnlyDictionary<string, TimeSpan> BuildHostMinIntervals()
    {
        var ncbiKey = Environment.GetEnvironmentVariable(PubMedSource.API_KEY_ENV_VAR);
        var ncbiInterval = string.IsNullOrWhiteSpace(ncbiKey)
            ? PubMedSource.KeylessMinInterval
            : PubMedSource.KeyedMinInterval;

        return new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["export.arxiv.org"] = TimeSpan.FromSeconds(3),
            [PubMedSource.Host] = ncbiInterval,
        };
    }

    public static HttpClient Create(IViceLogger logger)
    {
        return SafeHttpClient.Create(logger,
                                     ResolveUserAgent(),
                                     requestTimeout: RequestTimeout,
                                     maxResponseContentBufferBytes: MaxResponseContentBufferBytes,
                                     hostMinIntervals: BuildHostMinIntervals());
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
        var contact = ResolveContactEmail();
        if (contact is not null)
        {
            return $"Vice/{version} (+{ProjectUrl}; mailto:{contact})";
        }

        return $"Vice/{version} (+{ProjectUrl})";
    }

    internal static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    internal static string? ResolveContactEmail()
    {
        var contact = Environment.GetEnvironmentVariable(ContactEmailEnvVar);
        if (string.IsNullOrWhiteSpace(contact))
        {
            return null;
        }

        var trimmed = contact.Trim();
        if (!IsPlausibleEmail(trimmed))
        {
            return null;
        }

        return trimmed;
    }

    private static bool IsPlausibleEmail(string value)
    {
        var at = value.IndexOf('@');
        if (at <= 0
            || at != value.LastIndexOf('@')
            || at == value.Length - 1)
        {
            return false;
        }

        var domain = value.AsSpan(at + 1);
        if (!domain.Contains('.')
            || value.AsSpan().ContainsAny(' ', '\t'))
        {
            return false;
        }

        return true;
    }
}
