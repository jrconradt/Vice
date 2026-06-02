using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vice.Logging;
using Vice.Net.Requests.Grpc;
using Vice.Net.Research;
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
            Assert.Contains("lab.freya.cintile.io/atelier/vice", ua);
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

    [Fact]
    public void Insecure_callback_without_pin_accepts_any_certificate()
    {
        var original = Environment.GetEnvironmentVariable(GrpcConnectionManager.PinnedCertEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(GrpcConnectionManager.PinnedCertEnvVar, null);

            var callback = GrpcConnectionManager.BuildInsecureValidationCallback(NullViceLogger.Instance);
            using var cert = SelfSigned("CN=any");

            Assert.True(callback(this, cert, null, SslPolicyErrors.RemoteCertificateChainErrors));
        }
        finally
        {
            Environment.SetEnvironmentVariable(GrpcConnectionManager.PinnedCertEnvVar, original);
        }
    }

    [Fact]
    public void Insecure_callback_with_pin_accepts_only_matching_certificate()
    {
        var original = Environment.GetEnvironmentVariable(GrpcConnectionManager.PinnedCertEnvVar);
        try
        {
            using var expected = SelfSigned("CN=expected");
            using var attacker = SelfSigned("CN=attacker");

            var pin = Convert.ToHexString(SHA256.HashData(expected.GetRawCertData()));
            Environment.SetEnvironmentVariable(GrpcConnectionManager.PinnedCertEnvVar, pin);

            var callback = GrpcConnectionManager.BuildInsecureValidationCallback(NullViceLogger.Instance);

            Assert.True(callback(this, expected, null, SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(callback(this, attacker, null, SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(callback(this, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(GrpcConnectionManager.PinnedCertEnvVar, original);
        }
    }

    private static X509Certificate2 SelfSigned(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5));
    }
}
