using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Sites;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace FluentCMS.Plugins.Mcp.Tests;

// The host the tests of a class share: the MCP plugin and the sample plugin, both discovered, on one content type. No
// queue has workers, so a test drains the outbox itself when it reads the activity log.
public sealed class McpFixture : IAsyncDisposable
{
    public FluentCmsTestHost Host { get; } = McpTesting.Host();

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

internal static class McpTesting
{
    // The revision whose clients connect with the initialize handshake.
    public const string InitializeProtocol = "2025-11-25";

    public const string McpScope = "mcp";

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static FluentCmsTestHost Host() =>
        new FluentCmsTestHost()
            .WithContentTypes<Article>()
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });

    // The protocol's own client over the test server, with the credentials of the HTTP client.
    public static Task<McpClient> ConnectAsync(HttpClient http, Guid? site = null, string? protocolVersion = null)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        };
        if (site is { } named)
        {
            options.AdditionalHeaders = new Dictionary<string, string> { ["X-Cms-Site"] = named.ToString() };
        }

        return McpClient.CreateAsync(
            new HttpClientTransport(options, http, loggerFactory: null, ownsHttpClient: false),
            new McpClientOptions { ProtocolVersion = protocolVersion, ClientInfo = new Implementation { Name = "fluentcms-tests", Version = "1.0.0" } },
            cancellationToken: Token);
    }

    public static async Task<HttpClient> KeyAsync(FluentCmsTestHost host, ApiKeyClass keyClass = ApiKeyClass.Management, string role = BuiltInRoles.SuperAdminName) =>
        host.AsKey(await host.CreateApiKeyAsync(keyClass, role, cancellationToken: Token));

    // A bearer token of the test client for a new user of the role, with the mcp scope.
    public static async Task<(HttpClient Client, TestUser User)> UserAsync(FluentCmsTestHost host, string role = BuiltInRoles.SuperAdminName, params string[] scopes)
    {
        var user = await host.CreateUserAsync(role, cancellationToken: Token);
        return (await host.AsUserAsync(user, scopes.Length > 0 ? scopes : ["openid", McpScope]), user);
    }

    // A raw request of the protocol, for what the client hides: the status and headers of a refusal.
    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid? site = null, string path = "/mcp")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent("""{ "jsonrpc": "2.0", "id": 1, "method": "ping" }""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (site is { } named)
        {
            request.Headers.Add("X-Cms-Site", named.ToString());
        }

        return await client.SendAsync(request, Token);
    }

    public static async Task<JsonNode> JsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;

    public static async Task<string> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response))["code"]!.GetValue<string>();

    public static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    public static string? Code(CallToolResult result) => result.Meta?[McpRequests.CodeMeta]?.GetValue<string>();

    public static async Task<Guid> CreateSiteAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync("Docs", $"docs-{Guid.NewGuid():N}", "en", Token)).Id;
    }

    // As the host's own code, which the plugin administration takes as allowed.
    public static async Task SetEnabledAsync(FluentCmsTestHost host, string plugin, Guid site, bool enabled)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        await scope.ServiceProvider.GetRequiredService<PluginAdministration>().SetEnabledAsync(plugin, site, enabled, Token);
    }

    public static async Task DrainOutboxAsync(FluentCmsTestHost host)
    {
        while (await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }
    }

    // The authorization code flow of the test client, as an MCP client runs it: with the resource parameter of RFC 8707 on
    // the authorization request and on the token request. It returns the access token, or the error the server answered.
    public static async Task<(string? AccessToken, string? Error)> AuthorizeAsync(FluentCmsTestHost host, TestUser user, string scope, string? resource)
    {
        using var client = host.CreateSecureClient();
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        Dictionary<string, string?> query = new()
        {
            ["client_id"] = FluentCmsTestHost.OAuthClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = FluentCmsTestHost.OAuthRedirectUri,
            ["scope"] = scope,
            ["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
            ["state"] = "test",
        };
        if (resource is not null)
        {
            query["resource"] = resource;
        }

        using var authorize = new HttpRequestMessage(HttpMethod.Get, "/connect/authorize" + QueryString.Create(query));
        authorize.Headers.Add("Cookie", await host.SignInCookieAsync(user));
        using var answered = await client.SendAsync(authorize, Token);
        answered.StatusCode.ShouldBeRedirect();

        // A request the server refuses while it validates it goes to the error page with the error, and not to the client.
        var returned = QueryHelpers.ParseQuery(new Uri(client.BaseAddress!, answered.Headers.Location!).Query);
        if (!returned.TryGetValue("code", out var code))
        {
            return (null, returned["error"].ToString());
        }

        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code.ToString(),
            ["redirect_uri"] = FluentCmsTestHost.OAuthRedirectUri,
            ["client_id"] = FluentCmsTestHost.OAuthClientId,
            ["code_verifier"] = verifier,
        };
        if (resource is not null)
        {
            form["resource"] = resource;
        }

        using var content = new FormUrlEncodedContent(form);
        using var token = await client.PostAsync(new Uri("/connect/token", UriKind.Relative), content, Token);
        var body = await JsonAsync(token);
        return token.IsSuccessStatusCode ? (body["access_token"]!.GetValue<string>(), null) : (null, body["error"]!.GetValue<string>());
    }

    private static void ShouldBeRedirect(this HttpStatusCode status)
    {
        if (status != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"The authorize endpoint answered {(int)status}.");
        }
    }
}
