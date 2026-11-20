using System.Buffers.Text;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentCMS.Activity;
using FluentCMS.Agents;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Sites;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;
using Shouldly;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The host the tests of the MCP clients share. Every test signs in several times, and the buckets refill only when the
// test clock moves, so the auth limit is out of the way; the limit of registrations per address is the tests' own.
public sealed class McpClientFixture : IAsyncDisposable
{
    public FluentCmsTestHost Host { get; } = McpTesting.Host()
        .WithServices(services => services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000));

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

// The host of the tests that cross two changes of a client: the same host, with the application manager of OpenIddict
// behind hooks that a test arms for one call.
public sealed class McpClientHookFixture : IAsyncDisposable
{
    public McpClientHookFixture()
    {
        Host = McpTesting.Host().WithServices(services =>
        {
            services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000);
            // The manager of the interface is resolved by its closed type, which comes before OpenIddict's open registration.
            services.AddSingleton(Hooks);
            services.AddScoped<OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication<Guid>>, HookedApplicationManager>();
        });
    }

    public FluentCmsTestHost Host { get; }

    public ApplicationManagerHooks Hooks { get; } = new();

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

// What runs, once, in place of the next call of the application manager: work before the next read of a client's
// properties, which then answers what it read before the work, as a request that read them just before a change does;
// or a refusal of the next update, as when another change of the application got there first.
public sealed class ApplicationManagerHooks
{
    private Func<Task>? _beforeRead;
    private int _failUpdate;

    public void BeforeNextPropertiesRead(Func<Task> work) => _beforeRead = work;

    public void FailNextUpdate() => _failUpdate = 1;

    internal Func<Task>? TakeBeforeRead() => Interlocked.Exchange(ref _beforeRead, null);

    internal bool TakeFailUpdate() => Interlocked.Exchange(ref _failUpdate, 0) == 1;
}

public sealed class HookedApplicationManager(
    IOpenIddictApplicationCache<OpenIddictEntityFrameworkCoreApplication<Guid>> cache,
    ILogger<OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication<Guid>>> logger,
    IOptionsMonitor<OpenIddictCoreOptions> options,
    IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication<Guid>> store,
    ApplicationManagerHooks hooks) : OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication<Guid>>(cache, logger, options, store)
{
    public override async ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictEntityFrameworkCoreApplication<Guid> application, CancellationToken cancellationToken = default)
    {
        var properties = await base.GetPropertiesAsync(application, cancellationToken);
        if (hooks.TakeBeforeRead() is { } work)
        {
            await work();
        }

        return properties;
    }

    // Every update of an application ends here, the one with a descriptor included.
    public override ValueTask UpdateAsync(OpenIddictEntityFrameworkCoreApplication<Guid> application, CancellationToken cancellationToken = default) =>
        hooks.TakeFailUpdate()
            ? ValueTask.FromException(new OpenIddictExceptions.ConcurrencyException("The application was changed by another request."))
            : base.UpdateAsync(application, cancellationToken);
}

// What an authorization request came to: a code at the client's redirect URI, the error the client or the error page
// was given, or the hosted page the flow stopped at.
internal sealed record Authorized(string? Code, string? Error, string? StoppedAt, string Verifier);

// An MCP client and the browser of its user: it comes from one client address, keeps cookies, registers itself, sends
// the user through the authorization code flow with PKCE, the consent page included, and redeems the code.
internal sealed partial class McpAgent : IDisposable
{
    // Where a native MCP client listens, which no test ever calls.
    public const string RedirectUri = "http://127.0.0.1:33418/callback";

    public const string Scopes = "openid mcp offline_access";

    private static readonly Uri Base = new("https://localhost/");

    private readonly CookieContainer _cookies = new();
    private readonly FluentCmsTestHost _host;
    private readonly HttpMessageHandler _server;
    private readonly HttpClient _client;

    public McpAgent(FluentCmsTestHost host, string address)
    {
        _host = host;
        _server = ((TestServer)host.Services.GetRequiredService<IServer>()).CreateHandler(context => context.Connection.RemoteIpAddress = IPAddress.Parse(address));
        _client = new HttpClient(new CookieHandler(_cookies, _server), disposeHandler: false) { BaseAddress = Base };
    }

    public HttpClient Http => _client;

    public static string Registration(string name = "Test agent", string redirectUri = RedirectUri) =>
        $$"""{ "client_name": "{{name}}", "redirect_uris": ["{{redirectUri}}"], "grant_types": ["authorization_code", "refresh_token"], "response_types": ["code"], "token_endpoint_auth_method": "none" }""";

    public async Task<HttpResponseMessage> RegisterAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _client.PostAsync(new Uri("/connect/register", UriKind.Relative), content, Token);
    }

    // The client id of a registration that must succeed.
    public async Task<string> RegisteredAsync(string name = "Test agent")
    {
        using var response = await RegisterAsync(Registration(name));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        return (await JsonAsync(response))["client_id"]!.GetValue<string>();
    }

    public async Task SignInAsync(TestUser user) => _cookies.SetCookies(Base, await _host.SignInCookieAsync(user));

    public async Task<Authorized> AuthorizeAsync(string clientId, string scope = Scopes, bool allow = true, string redirectUri = RedirectUri)
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var next = "/connect/authorize" + QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = scope,
            ["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
            ["state"] = "test",
            ["resource"] = "https://localhost/mcp",
        });

        for (var hops = 0; hops < 10; hops++)
        {
            var path = next.Split('?')[0];
            if (path == "/account/error")
            {
                return new Authorized(null, QueryHelpers.ParseQuery(next[(next.IndexOf('?', StringComparison.Ordinal) + 1)..])["error"].ToString(), path, verifier);
            }

            if (path is not ("/connect/authorize" or "/account/consent"))
            {
                return new Authorized(null, null, path, verifier);
            }

            using var response = path == "/account/consent"
                ? await ConsentAsync(next, allow)
                : await _client.GetAsync(new Uri(next, UriKind.Relative), Token);
            if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location is not { } location)
            {
                throw new ShouldAssertException($"{next} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Token)}");
            }

            if (location.IsAbsoluteUri && location.AbsoluteUri.StartsWith(redirectUri, StringComparison.Ordinal))
            {
                var returned = QueryHelpers.ParseQuery(location.Query);
                return new Authorized(returned.TryGetValue("code", out var code) ? code.ToString() : null, returned.TryGetValue("error", out var error) ? error.ToString() : null, null, verifier);
            }

            next = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
        }

        throw new ShouldAssertException("Too many redirects.");
    }

    // Opens the consent page and posts its form, as its buttons do. A page that does not show answers for itself.
    public async Task<HttpResponseMessage> ConsentAsync(string consentUrl, bool allow)
    {
        var page = await _client.GetAsync(new Uri(consentUrl, UriKind.Relative), Token);
        if (page.StatusCode != HttpStatusCode.OK)
        {
            return page;
        }

        using var shown = page;
        var match = Antiforgery().Match(await page.Content.ReadAsStringAsync(Token));
        match.Success.ShouldBeTrue("The consent page has no antiforgery token.");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["decision"] = allow ? "allow" : "deny",
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value),
        });
        return await _client.PostAsync(new Uri(consentUrl, UriKind.Relative), form, Token);
    }

    public async Task<HttpResponseMessage> RedeemAsync(string clientId, Authorized authorized, string redirectUri = RedirectUri)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorized.Code.ShouldNotBeNull(),
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = authorized.Verifier,
            ["resource"] = "https://localhost/mcp",
        });
        return await _client.PostAsync(new Uri("/connect/token", UriKind.Relative), form, Token);
    }

    public async Task<HttpResponseMessage> RefreshAsync(string clientId, string refreshToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        });
        return await _client.PostAsync(new Uri("/connect/token", UriKind.Relative), form, Token);
    }

    // The whole flow for a client that is approved: the tokens of the user through the client.
    public async Task<JsonNode> TokensAsync(string clientId, TestUser user, string scope = Scopes)
    {
        await SignInAsync(user);
        var authorized = await AuthorizeAsync(clientId, scope);
        authorized.Code.ShouldNotBeNull($"The flow ended with {authorized.Error} at {authorized.StoppedAt}.");
        using var redeemed = await RedeemAsync(clientId, authorized);
        redeemed.StatusCode.ShouldBe(HttpStatusCode.OK, await redeemed.Content.ReadAsStringAsync(Token));
        return await JsonAsync(redeemed);
    }

    // A client of /mcp with a bearer token, from the agent's address.
    public HttpClient Bearer(string accessToken)
    {
        var client = new HttpClient(_server, disposeHandler: false) { BaseAddress = Base };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();

    private sealed class CookieHandler(CookieContainer cookies, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = cookies.GetCookieHeader(uri);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    cookies.SetCookies(uri, value);
                }
            }

            return response;
        }
    }
}

internal static class McpClientTesting
{
    public const string ClientsPath = "/api/management/v1/mcp/clients";

    // The instance defaults of the settings area, written as the host's own code.
    public static async Task SetPolicyAsync(FluentCmsTestHost host, McpClientRegistrationMode mode, string? defaultRole = null, int maxPending = 100, int perHour = 100, int maxClients = 1000)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        await scope.ServiceProvider.GetRequiredService<ISettings<McpSettings>>().SetDefaultAsync(
            new McpSettings { ClientRegistration = mode, DefaultClientRole = defaultRole, MaxPendingClients = maxPending, RegistrationsPerHour = perHour, MaxClients = maxClients },
            Token);
    }

    public static async Task<JsonArray> ListAsync(HttpClient admin)
    {
        using var response = await admin.GetAsync(new Uri(ClientsPath, UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await JsonAsync(response))["items"]!.AsArray();
    }

    // The client of the list with the OAuth client id, or null.
    public static async Task<JsonNode?> FindAsync(HttpClient admin, string clientId) =>
        (await ListAsync(admin)).FirstOrDefault(item => item!["clientId"]!.GetValue<string>() == clientId);

    public static async Task<Guid> IdOfAsync(HttpClient admin, string clientId) =>
        (await FindAsync(admin, clientId)).ShouldNotBeNull()["id"]!.GetValue<Guid>();

    public static async Task<HttpResponseMessage> ApproveAsync(HttpClient admin, Guid id, string? role)
    {
        using var content = new StringContent(role is null ? "{}" : $$"""{ "role": "{{role}}" }""", Encoding.UTF8, "application/json");
        return await admin.PostAsync(new Uri($"{ClientsPath}/{id:D}/approve", UriKind.Relative), content, Token);
    }

    public static Task<HttpResponseMessage> RejectAsync(HttpClient admin, Guid id) =>
        admin.PostAsync(new Uri($"{ClientsPath}/{id:D}/reject", UriKind.Relative), null, Token);

    public static Task<HttpResponseMessage> RevokeAsync(HttpClient admin, Guid id) =>
        admin.DeleteAsync(new Uri($"{ClientsPath}/{id:D}", UriKind.Relative), Token);

    // The body of an administrator's registration; a null leaves the member out.
    public static string Registration(string? name, string? role, params string[] redirectUris)
    {
        var body = new JsonObject { ["redirectUris"] = new JsonArray([.. redirectUris.Select(uri => JsonValue.Create(uri))]) };
        if (name is not null)
        {
            body["name"] = name;
        }

        if (role is not null)
        {
            body["role"] = role;
        }

        return body.ToJsonString();
    }

    public static async Task<HttpResponseMessage> RegisterAsync(HttpClient admin, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await admin.PostAsync(new Uri(ClientsPath, UriKind.Relative), content, Token);
    }

    // A client an administrator registered for the agent's redirect URI: its OAuth client id and its id in the store.
    public static async Task<(string ClientId, Guid Id)> RegisteredByAdminAsync(HttpClient admin, string role, string name = "Registered agent")
    {
        using var response = await RegisterAsync(admin, Registration(name, role, McpAgent.RedirectUri));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        var client = await JsonAsync(response);
        return (client["clientId"]!.GetValue<string>(), client["id"]!.GetValue<Guid>());
    }

    public static async Task<HttpResponseMessage> ChangeRoleAsync(HttpClient admin, Guid id, string? role)
    {
        using var content = new StringContent(role is null ? "{}" : $$"""{ "role": "{{role}}" }""", Encoding.UTF8, "application/json");
        return await admin.PutAsync(new Uri($"{ClientsPath}/{id:D}/role", UriKind.Relative), content, Token);
    }

    // The activity rows of a client, as a reader of the instance's activity sees them, once the outbox is drained.
    public static async Task<IReadOnlyList<ActivityItem>> RowsAsync(FluentCmsTestHost host, TestUser reader, Guid clientId)
    {
        await DrainOutboxAsync(host);
        return (await host.As(reader).RunAsync<IActivityQuery, PagedResult<ActivityItem>>(query =>
            query.QueryAsync(new ActivityFilter { TargetType = "mcp_client", TargetId = clientId.ToString("D"), IncludeInstance = true }, null, Token))).Items;
    }

    // The id in the store of a client of the configuration, which the Management API does not list.
    public static async Task<Guid> ApplicationIdAsync(FluentCmsTestHost host, string clientId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = (await applications.FindByClientIdAsync(clientId, Token)).ShouldNotBeNull();
        return Guid.Parse((await applications.GetIdAsync(application, Token))!);
    }

    // Registers a client and approves it with a role: its OAuth client id and its id in the store.
    public static async Task<(string ClientId, Guid Id)> ApprovedAsync(McpAgent agent, HttpClient admin, string role, string name = "Test agent")
    {
        var clientId = await agent.RegisteredAsync(name);
        var id = await IdOfAsync(admin, clientId);
        using var approved = await ApproveAsync(admin, id, role);
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync(Token));
        return (clientId, id);
    }
}
