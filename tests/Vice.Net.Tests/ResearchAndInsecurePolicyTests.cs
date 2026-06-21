using Vice.Logging;
using Vice.Research;
using Xunit;

namespace Vice.Net.Tests;

public sealed class ResearchAndInsecurePolicyTests
{
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        return new HttpClient(new StubHttpMessageHandler(responder));
    }

    [Fact]
    public async Task Arxiv_download_targets_the_export_api_host()
    {
        var arxiv = new ArxivSource();
        var target = await arxiv.ResolveDownloadAsync(
            Client(_ => StubHttpMessageHandler.Ok("{}", "application/json")),
            "2401.00001",
            null,
            CancellationToken.None,
            NullViceLogger.Instance);

        Assert.Equal("export.arxiv.org", target.Uri.Host);
        Assert.Equal("/pdf/2401.00001", target.Uri.AbsolutePath);
        Assert.Equal("pdf", target.Extension);
    }

    [Fact]
    public void Default_user_agent_carries_project_url_and_no_placeholder_repo()
    {
        var original = Environment.GetEnvironmentVariable(ResearchHttp.UserAgentEnvVar);
        var originalContact = Environment.GetEnvironmentVariable(ResearchHttp.ContactEmailEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ResearchHttp.UserAgentEnvVar, null);
            Environment.SetEnvironmentVariable(ResearchHttp.ContactEmailEnvVar, null);

            var ua = ResearchHttp.ResolveUserAgent();

            Assert.StartsWith("Vice/", ua);
            Assert.Contains("github.com/jrconradt/Vice", ua);
            Assert.DoesNotContain("github.com/vice-cli", ua);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ResearchHttp.UserAgentEnvVar, original);
            Environment.SetEnvironmentVariable(ResearchHttp.ContactEmailEnvVar, originalContact);
        }
    }

    [Fact]
    public void User_agent_env_var_overrides_default()
    {
        var original = Environment.GetEnvironmentVariable(ResearchHttp.UserAgentEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ResearchHttp.UserAgentEnvVar, "MyTool/9.9 (mailto:me@example.org)");
            Assert.Equal("MyTool/9.9 (mailto:me@example.org)", ResearchHttp.ResolveUserAgent());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ResearchHttp.UserAgentEnvVar, original);
        }
    }

    [Fact]
    public void Contact_email_env_var_is_embedded_in_default_user_agent()
    {
        var originalUa = Environment.GetEnvironmentVariable(ResearchHttp.UserAgentEnvVar);
        var originalContact = Environment.GetEnvironmentVariable(ResearchHttp.ContactEmailEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ResearchHttp.UserAgentEnvVar, null);
            Environment.SetEnvironmentVariable(ResearchHttp.ContactEmailEnvVar, "ops@example.org");

            var ua = ResearchHttp.ResolveUserAgent();

            Assert.Contains("mailto:ops@example.org", ua);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ResearchHttp.UserAgentEnvVar, originalUa);
            Environment.SetEnvironmentVariable(ResearchHttp.ContactEmailEnvVar, originalContact);
        }
    }

}
