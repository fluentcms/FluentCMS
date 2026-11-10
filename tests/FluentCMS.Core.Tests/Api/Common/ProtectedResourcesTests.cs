using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

// The URLs of a plugin's protected resources (RFC 9728), and which resource parameter (RFC 8707) names one of them.
public sealed class ProtectedResourcesTests
{
    private static readonly ProtectedResource Mcp = new("mcp", "/mcp", "mcp", "FluentCMS");

    [Theory]
    [InlineData("https://cms.example.com/cms/mcp", true)]
    [InlineData("HTTPS://CMS.EXAMPLE.COM/cms/mcp", true)]
    [InlineData("https://cms.example.com:443/cms/mcp", true)]
    [InlineData("https://cms.example.com/cms/MCP", false)]
    [InlineData("https://cms.example.com/cms/mcp/", false)]
    [InlineData("https://cms.example.com/cms/mcp?x=1", false)]
    [InlineData("https://cms.example.com/mcp", false)]
    [InlineData("http://cms.example.com/cms/mcp", false)]
    [InlineData("https://internal.example:8080/cms/mcp", false)]
    [InlineData("https://cms.example.com.evil.example/cms/mcp", false)]
    [InlineData("/cms/mcp", false)]
    [InlineData("fluentcms:management", false)]
    [InlineData("", false)]
    public void WithAPublicOriginAResourceIsNamedByItsPublicUrlWhateverTheRequestSays(string resource, bool named)
    {
        var resources = Resources("https://cms.example.com", "/cms");

        resources.Names(Request("http", "internal.example:8080", "/cms"), resource).ShouldBe(named);
    }

    [Theory]
    [InlineData("https://localhost:5001/mcp", true)]
    [InlineData("https://cms.example.com/mcp", false)]
    public void WithoutAPublicOriginTheUrlFollowsTheRequestAsTheIssuerDoes(string resource, bool named)
    {
        var resources = Resources(null, null);

        resources.Names(Request("https", "localhost:5001", ""), resource).ShouldBe(named);
    }

    [Fact]
    public void AHostWithoutProtectedResourcesNamesNone()
    {
        var resources = new ProtectedResources(Origin("https://cms.example.com", null));

        resources.Names(Request("https", "cms.example.com", ""), "https://cms.example.com/mcp").ShouldBeFalse();
    }

    [Fact]
    public void TheDocumentNamesTheResourceTheIssuerAndTheScope()
    {
        var document = Resources("https://cms.example.com", "/cms").Document(Request("http", "internal.example:8080", "/cms"), Mcp);

        document.ToJsonString().ShouldBe(
            """{"resource":"https://cms.example.com/cms/mcp","authorization_servers":["https://cms.example.com/cms"],"scopes_supported":["mcp"],"bearer_methods_supported":["header"],"resource_name":"FluentCMS"}""");
    }

    [Fact]
    public void AChallengeIsWrittenForA401AndForATokenWithoutTheScopeOnly()
    {
        var resources = Resources("https://cms.example.com", null);
        const string Metadata = "resource_metadata=\"https://cms.example.com/.well-known/oauth-protected-resource/mcp\"";

        Challenge(resources, 401, new SignInRequiredException("Sign in.")).ShouldBe($"Bearer {Metadata}, scope=\"mcp\"");
        Challenge(resources, 401, new RequestAuthenticationException("Refused.")).ShouldBe($"Bearer error=\"invalid_token\", {Metadata}, scope=\"mcp\"");
        Challenge(resources, 403, new ScopeDeniedException("No scope.")).ShouldBe($"Bearer error=\"insufficient_scope\", scope=\"mcp\", {Metadata}");

        // A permission the services refuse is no matter of the token's scope, and a 404 none of the caller's.
        Challenge(resources, 403, new InvalidOperationException()).ShouldBeNull();
        Challenge(resources, 404, new InvalidOperationException()).ShouldBeNull();
    }

    private static string? Challenge(ProtectedResources resources, int status, Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("cms.example.com");
        resources.Challenge(context, Mcp, new ApiProblem(status, "FCMS0000", "Title", "Detail"), exception);
        return context.Response.Headers.TryGetValue(HeaderNames.WWWAuthenticate, out var value) ? value.ToString() : null;
    }

    private static ProtectedResources Resources(string? origin, string? pathBase)
    {
        var resources = new ProtectedResources(Origin(origin, pathBase));
        resources.Add(Mcp);
        return resources;
    }

    private static PublicOrigin Origin(string? origin, string? pathBase)
    {
        var resolved = new PublicOrigin();
        resolved.Resolve(origin, new ReverseProxyOptions { PathBase = pathBase }, environment: null);
        return resolved;
    }

    private static HttpRequest Request(string scheme, string host, string pathBase)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.PathBase = pathBase;
        return context.Request;
    }
}
