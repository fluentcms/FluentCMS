using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Authorization;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// How an MCP client finds the authorization server and gets a token for /mcp: the protected resource metadata of
// RFC 9728, and the resource parameter of RFC 8707 on the way to the token.
public sealed class McpResourceMetadataTests(McpFixture fixture) : IClassFixture<McpFixture>
{
    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/oauth-protected-resource/mcp")]
    public async Task TheMetadataNamesTheMcpUrlAndTheAuthorizationServerToAnyone(string path)
    {
        using var anonymous = fixture.Host.CreateSecureClient();

        using var response = await anonymous.GetAsync(new Uri(path, UriKind.Relative), Token);
        using var discovery = await anonymous.GetAsync(new Uri("/.well-known/openid-configuration", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var document = await JsonAsync(response);
        document["resource"]!.GetValue<string>().ShouldBe("https://localhost/mcp");
        Strings(document["authorization_servers"]!).ShouldBe([(await JsonAsync(discovery))["issuer"]!.GetValue<string>()]);
        Strings(document["scopes_supported"]!).ShouldBe(["mcp"]);
        Strings(document["bearer_methods_supported"]!).ShouldBe(["header"]);
        document["resource_name"]!.GetValue<string>().ShouldBe("FluentCMS");
    }

    // Behind a reverse proxy the URLs come from the public origin and the path base, whatever host the request names.
    [Fact]
    public async Task TheMetadataAndTheChallengeAreBuiltFromThePublicOriginBehindAReverseProxy()
    {
        await using var host = Host().Configure(cms =>
        {
            cms.ReverseProxy.PathBase = "/cms";
            cms.PublicOrigin = "https://cms.example.com";
        });
        using var client = host.CreateSecureClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/cms/.well-known/oauth-protected-resource/mcp");
        request.Headers.Host = "internal.example:8080";
        using var response = await client.SendAsync(request, Token);
        using var root = await client.GetAsync(new Uri("/cms/.well-known/oauth-protected-resource", UriKind.Relative), Token);
        using var discovery = await client.GetAsync(new Uri("/cms/.well-known/openid-configuration", UriKind.Relative), Token);
        using var refused = await PostAsync(client, path: "/cms/mcp");
        using var outside = await client.GetAsync(new Uri("/.well-known/oauth-protected-resource/mcp", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var document = await JsonAsync(response);
        document["resource"]!.GetValue<string>().ShouldBe("https://cms.example.com/cms/mcp");
        Strings(document["authorization_servers"]!).ShouldBe(["https://cms.example.com/cms"]);
        Strings(document["authorization_servers"]!).ShouldBe([(await JsonAsync(discovery))["issuer"]!.GetValue<string>()]);
        (await JsonAsync(root))["resource"]!.GetValue<string>().ShouldBe("https://cms.example.com/cms/mcp");
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refused.Headers.WwwAuthenticate.ShouldHaveSingleItem().Parameter!.ShouldContain("resource_metadata=\"https://cms.example.com/cms/.well-known/oauth-protected-resource/mcp\"");
        outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // What an MCP client does: it names the URL the metadata gave it on the authorization request and on the token request.
    [Fact]
    public async Task TheAuthorizationServerTakesTheMcpUrlAsTheResourceAndTheTokenWorksAtMcp()
    {
        var host = fixture.Host;
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

        var (token, error) = await AuthorizeAsync(host, user, "openid mcp", "https://localhost/mcp");

        error.ShouldBeNull();
        using var http = host.CreateSecureClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        (await client.CallToolAsync(SampleTools.WhoAmI, cancellationToken: Token)).IsError.ShouldBe(false);
    }

    [Theory]
    [InlineData("https://elsewhere.example/mcp")]
    [InlineData("https://localhost/api/management/v1")]
    [InlineData("https://localhost/MCP")]
    [InlineData("https://localhost/mcp/")]
    public async Task TheAuthorizationServerRefusesAnyOtherResource(string resource)
    {
        var host = fixture.Host;
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

        var (token, error) = await AuthorizeAsync(host, user, "openid mcp", resource);

        token.ShouldBeNull();
        error.ShouldBe("invalid_target");
    }

    // The token request is checked on its own, so a code got without a resource cannot be redeemed for another one.
    [Fact]
    public async Task TheTokenEndpointRefusesAnotherResourceToo()
    {
        var host = fixture.Host;
        using var client = host.CreateSecureClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = "not-a-code",
            ["redirect_uri"] = FluentCmsTestHost.OAuthRedirectUri,
            ["client_id"] = FluentCmsTestHost.OAuthClientId,
            ["code_verifier"] = new string('v', 43),
            ["resource"] = "https://elsewhere.example/mcp",
        });

        using var response = await client.PostAsync(new Uri("/connect/token", UriKind.Relative), content, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(response))["error"]!.GetValue<string>().ShouldBe("invalid_target");
    }

    [Fact]
    public async Task TheMcpScopeIsOfferedAndDescribedOnTheConsentPage()
    {
        using var client = fixture.Host.CreateSecureClient();

        using var discovery = await client.GetAsync(new Uri("/.well-known/openid-configuration", UriKind.Relative), Token);

        Strings((await JsonAsync(discovery))["scopes_supported"]!).ShouldContain(TokenScopes.Mcp);
        ConsentModel.Describe(TokenScopes.Mcp).ShouldBe("Read and change content as an AI agent or tool, as far as your roles allow");
    }

    private static string[] Strings(JsonNode node) => [.. node.AsArray().Select(item => item!.GetValue<string>())];
}
