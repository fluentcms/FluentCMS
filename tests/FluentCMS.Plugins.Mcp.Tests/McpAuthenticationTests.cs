using System.Net;
using System.Net.Http.Headers;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// Who /mcp takes: a bearer token with the mcp scope, or an API key by its class. A refusal carries the challenge that
// leads an MCP client to the authorization server.
public sealed class McpAuthenticationTests(McpFixture fixture) : IClassFixture<McpFixture>
{
    private const string Metadata = "resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"";

    [Fact]
    public async Task ARequestWithNoCredentialsGets401WithAChallengeThatNamesTheResourceMetadata()
    {
        using var anonymous = fixture.Host.CreateClient();

        using var response = await PostAsync(anonymous);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).ShouldBe("FCMS3102");
        var challenge = response.Headers.WwwAuthenticate.ShouldHaveSingleItem();
        challenge.Scheme.ShouldBe("Bearer");
        challenge.Parameter.ShouldBe($"{Metadata}, scope=\"mcp\"");
    }

    [Fact]
    public async Task CredentialsThatAreRefusedGet401WithInvalidToken()
    {
        using var badKey = fixture.Host.CreateClient();
        badKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", "fcms_mk_" + new string('a', 43));
        using var badToken = fixture.Host.CreateSecureClient();
        badToken.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        using var key = await PostAsync(badKey);
        using var token = await PostAsync(badToken);

        key.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        key.Headers.WwwAuthenticate.ShouldHaveSingleItem().Parameter.ShouldBe($"error=\"invalid_token\", {Metadata}, scope=\"mcp\"");
        token.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        token.Headers.WwwAuthenticate.ShouldHaveSingleItem().Parameter!.ShouldStartWith("error=\"invalid_token\", resource_metadata=\"https://localhost/");
    }

    [Fact]
    public async Task ATokenWithoutTheMcpScopeGets403WithInsufficientScope()
    {
        var (http, _) = await UserAsync(fixture.Host, BuiltInRoles.SuperAdminName, "openid", "management");
        using (http)
        {
            using var response = await PostAsync(http);

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(response)).ShouldBe("FCMS3103");
            var challenge = response.Headers.WwwAuthenticate.ShouldHaveSingleItem();
            challenge.Scheme.ShouldBe("Bearer");
            challenge.Parameter.ShouldBe("error=\"insufficient_scope\", scope=\"mcp\", resource_metadata=\"https://localhost/.well-known/oauth-protected-resource/mcp\"");
        }
    }

    // The mcp scope manages through /mcp only: the same token is not a Management token.
    [Fact]
    public async Task ATokenWithOnlyTheMcpScopeIsRefusedByTheManagementApi()
    {
        var (http, _) = await UserAsync(fixture.Host);
        using (http)
        {
            using var mcp = await PostAsync(http);
            using var management = await http.GetAsync(new Uri("/api/management/v1/content/article", UriKind.Relative), Token);

            mcp.StatusCode.ShouldBe(HttpStatusCode.OK);
            management.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    // A preview token carries the kind of the principal that minted it, a key's included, and reads one entry. It is no key.
    [Fact]
    public async Task APreviewTokenIsRefusedAsATokenWithoutTheScope()
    {
        var host = fixture.Host;
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var article = await host.As(key).Content<Article>().CreateAsync(new Article { Title = "Previewed" }, Token);
        using var keyHttp = host.AsKey(key);
        using var minted = await keyHttp.PostAsync(new Uri($"/api/management/v1/content/article/{article.Id}/preview-token", UriKind.Relative), null, Token);
        minted.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var http = host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(PreviewTokenAuthentication.Scheme, (await JsonAsync(minted))["token"]!.GetValue<string>());

        using var response = await PostAsync(http);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.WwwAuthenticate.ShouldHaveSingleItem().Parameter!.ShouldStartWith("error=\"insufficient_scope\"");
    }

    // A key holds no OAuth scope. Its class is what the engine reads: a Delivery key reads what is published and writes nothing.
    [Fact]
    public async Task ADeliveryKeyReadsWhatItsClassReadsAndAPreviewKeyDraftsToo()
    {
        var host = fixture.Host;
        var editor = host.As(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        var live = await editor.Content<Article>().CreateAsync(new Article { Title = "Live " + Guid.NewGuid().ToString("N")[..8] }, Token);
        await editor.Content<Article>().PublishAsync(live.Id, cancellationToken: Token);
        var draft = await editor.Content<Article>().CreateAsync(new Article { Title = "Draft " + Guid.NewGuid().ToString("N")[..8] }, Token);
        using var deliveryHttp = await KeyAsync(host, ApiKeyClass.Delivery, BuiltInRoles.EditorName);
        using var previewHttp = await KeyAsync(host, ApiKeyClass.Preview, BuiltInRoles.EditorName);
        await using var delivery = await ConnectAsync(deliveryHttp, protocolVersion: InitializeProtocol);
        await using var preview = await ConnectAsync(previewHttp, protocolVersion: InitializeProtocol);

        var published = await delivery.CallToolAsync(SampleTools.EntryTitles, Arguments("article", status: "published"), cancellationToken: Token);
        var refusedDrafts = await delivery.CallToolAsync(SampleTools.EntryTitles, Arguments("article", status: "draft"), cancellationToken: Token);
        var refusedWrite = await delivery.CallToolAsync(SampleTools.EntryCreate, Arguments("article", title: "Nope"), cancellationToken: Token);
        var drafts = await preview.CallToolAsync(SampleTools.EntryTitles, Arguments("article", status: "draft"), cancellationToken: Token);
        var previewWrite = await preview.CallToolAsync(SampleTools.EntryCreate, Arguments("article", title: "Nope"), cancellationToken: Token);

        published.IsError.ShouldBe(false);
        Text(published).ShouldContain(live.Title);
        Text(published).ShouldNotContain(draft.Title);
        refusedDrafts.IsError.ShouldBe(true);
        refusedWrite.IsError.ShouldBe(true);
        Code(refusedWrite).ShouldBe("FCMS3103");
        drafts.IsError.ShouldBe(false);
        Text(drafts).ShouldContain(draft.Title);
        Code(previewWrite).ShouldBe("FCMS3103");
    }

    private static Dictionary<string, object?> Arguments(string type, string? title = null, string? status = null)
    {
        Dictionary<string, object?> arguments = new() { ["type"] = type };
        if (title is not null)
        {
            arguments["title"] = title;
        }

        if (status is not null)
        {
            arguments["status"] = status;
        }

        return arguments;
    }
}
