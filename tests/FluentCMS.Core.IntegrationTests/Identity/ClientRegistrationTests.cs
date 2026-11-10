using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Agents;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Sites;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// Dynamic client registration (RFC 7591) as core serves it: only while a host has a registration policy, which the MCP
// plugin brings, and with the role of a registered client held on every surface that takes its token. The plugin's own
// tests cover the policy settings and the Management endpoints.
public sealed class ClientRegistrationTests
{
    private const string RedirectUri = "http://127.0.0.1:41000/callback";
    private const string Registration = """{ "client_name": "Agent", "redirect_uris": ["http://127.0.0.1:41000/callback"], "scope": "openid mcp" }""";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutARegistrationPolicyNoClientRegistersItselfAndDiscoveryNamesNoEndpoint()
    {
        await using var host = AuthorizationServerTests.NewHost();
        using var client = host.CreateSecureClient();

        using var response = await RegisterAsync(client);
        using var discovery = await client.GetAsync("/.well-known/openid-configuration", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await JsonAsync(response)).GetProperty("code").GetString().ShouldBe(ApiErrorCodes.NotFound);
        (await JsonAsync(discovery)).TryGetProperty("registration_endpoint", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task APolicyThatIsOffAnswers404AndOneThatIsNotIsNamedInTheDiscoveryDocument()
    {
        var policy = new Policy(McpClientRegistrationMode.Off);
        await using var host = NewHost(policy);
        using var client = host.CreateSecureClient();

        using var off = await RegisterAsync(client);
        using var offDiscovery = await client.GetAsync("/.well-known/openid-configuration", Token);
        policy.Mode = McpClientRegistrationMode.Approval;
        using var on = await RegisterAsync(client);
        using var onDiscovery = await client.GetAsync("/.well-known/openid-configuration", Token);

        off.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await JsonAsync(offDiscovery)).TryGetProperty("registration_endpoint", out _).ShouldBeFalse();
        on.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await JsonAsync(onDiscovery)).GetProperty("registration_endpoint").GetString().ShouldBe("https://localhost/connect/register");
    }

    [Theory]
    [InlineData("text/plain", """{ "redirect_uris": ["https://agent.example.com/cb"] }""")]
    [InlineData("application/json", """{ "redirect_uris": ["https://agent.example.com/cb"] """)]
    [InlineData("application/json", "")]
    public async Task ABodyThatIsNotJsonIsRefusedWithTheErrorOfTheRegistrationProtocol(string contentType, string body)
    {
        await using var host = NewHost(new Policy(McpClientRegistrationMode.Approval));
        using var client = host.CreateSecureClient();
        using var content = new StringContent(body, Encoding.UTF8, contentType);

        using var response = await client.PostAsync("/connect/register", content, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(response)).GetProperty("error").GetString().ShouldBe("invalid_client_metadata");
    }

    [Fact]
    public async Task ARegistrationLargerThanTheLimitIsRefused()
    {
        await using var host = NewHost(new Policy(McpClientRegistrationMode.Approval));
        using var client = host.CreateSecureClient();
        using var content = new StringContent($$"""{ "redirect_uris": ["https://agent.example.com/cb"], "padding": "{{new string('x', DynamicClientRegistration.MaxBodyBytes)}}" }""", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/connect/register", content, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(response)).GetProperty("error").GetString().ShouldBe("invalid_client_metadata");
    }

    [Fact]
    public async Task APendingClientGetsNoCodeNoConsentAndNoToken()
    {
        await using var host = NewHost(new Policy(McpClientRegistrationMode.Approval));
        using var client = host.CreateSecureClient();
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var clientId = await RegisteredAsync(client);

        var (code, error, _) = await AuthorizeAsync(host, client, editor, clientId);
        var consented = await WithScopeAsync(host, services => services.GetRequiredService<ClientConsents>().GrantAsync(editor.Id, clientId, ["openid", "mcp"], Token));

        code.ShouldBeNull();
        error.ShouldBe(ConnectEndpoints.ClientPendingApproval);
        consented.ShouldBeFalse();
    }

    // The role of the client is a claim of its tokens, which the evaluator reads: what the user's roles allow and the
    // client's role does not is refused on the Delivery API too, which takes the token as any bearer token.
    [Fact]
    public async Task TheTokenOfAnApprovedClientCarriesItsRoleWhichHoldsOnEverySurface()
    {
        await using var host = NewHost(new Policy(McpClientRegistrationMode.Approval));
        using var client = host.CreateSecureClient();
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var nothing = await host.CreateRoleAsync("Nothing", ["sites.manage"], Token);
        var narrow = await RegisteredAsync(client);
        var wide = await RegisteredAsync(client);
        await ApproveAsync(host, narrow, nothing.ToString("D"));
        await ApproveAsync(host, wide, BuiltInRoles.EditorName);

        var narrowTokens = await SignInAsync(host, client, editor, narrow);
        var wideTokens = await SignInAsync(host, client, editor, wide);
        using var throughNarrow = Bearer(host, narrowTokens.AccessToken);
        using var throughWide = Bearer(host, wideTokens.AccessToken);
        using var refused = await throughNarrow.GetAsync("/api/delivery/v1/note", Token);
        using var allowed = await throughWide.GetAsync("/api/delivery/v1/note", Token);
        using var management = await throughWide.GetAsync("/api/management/v1/content/note", Token);

        Payload(narrowTokens.AccessToken).GetProperty(CmsClaimTypes.ClientRole).GetString().ShouldBe(nothing.ToString("D"));
        Payload(wideTokens.AccessToken).GetProperty(CmsClaimTypes.ClientRole).GetString().ShouldBe(BuiltInRoles.Editor.ToString("D"));
        Payload(wideTokens.AccessToken).GetProperty("scope").GetString().ShouldBe("openid mcp");
        // Delivery answers a type the caller may not read as it answers one that does not exist.
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        management.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A token of the test client, which did not register itself, carries no client role and is not limited.
        var own = await host.SignInAsync(editor, "openid", "mcp");
        Payload(own.AccessToken).TryGetProperty(CmsClaimTypes.ClientRole, out _).ShouldBeFalse();
    }

    private static FluentCmsTestHost NewHost(Policy policy) => AuthorizationServerTests.NewHost()
        .WithServices(services =>
        {
            services.AddSingleton<IMcpClientRegistrationPolicy>(policy);
            services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000);
        });

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client)
    {
        using var content = new StringContent(Registration, Encoding.UTF8, "application/json");
        return await client.PostAsync("/connect/register", content, Token);
    }

    private static async Task<string> RegisteredAsync(HttpClient client)
    {
        using var response = await RegisterAsync(client);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await JsonAsync(response)).GetProperty("client_id").GetString()!;
    }

    // As the host's own code, which the MCP plugin's service stands in front of with its permission.
    private static async Task ApproveAsync(FluentCmsTestHost host, string clientId, string role)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        var clients = scope.ServiceProvider.GetRequiredService<IMcpClients>();
        var id = (await clients.ListAsync(Token)).Single(client => client.ClientId == clientId).Id;
        (await clients.ApproveAsync(id, role, Token)).Outcome.ShouldBe(McpClientChangeOutcome.Done);
    }

    private static async Task<TestTokens> SignInAsync(FluentCmsTestHost host, HttpClient client, TestUser user, string clientId)
    {
        // The user's consent, which the hosted page records; the tests of the pages cover the page itself.
        (await WithScopeAsync(host, services => services.GetRequiredService<ClientConsents>().GrantAsync(user.Id, clientId, ["openid", "mcp"], Token))).ShouldBeTrue();
        var (code, error, verifier) = await AuthorizeAsync(host, client, user, clientId);
        code.ShouldNotBeNull(error);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        });
        using var response = await client.PostAsync("/connect/token", form, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        var body = await JsonAsync(response);
        return new TestTokens(body.GetProperty("access_token").GetString()!, null, null);
    }

    private static async Task<(string? Code, string? Error, string Verifier)> AuthorizeAsync(FluentCmsTestHost host, HttpClient client, TestUser user, string clientId)
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/authorize" + QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = "openid mcp",
            ["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
        }));
        request.Headers.Add("Cookie", await host.SignInCookieAsync(user));
        using var response = await client.SendAsync(request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var returned = QueryHelpers.ParseQuery(new Uri(client.BaseAddress!, response.Headers.Location!).Query);
        return (returned.TryGetValue("code", out var code) ? code.ToString() : null, returned.TryGetValue("error", out var error) ? error.ToString() : null, verifier);
    }

    private static HttpClient Bearer(FluentCmsTestHost host, string accessToken)
    {
        var client = host.CreateSecureClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task<T> WithScopeAsync<T>(FluentCmsTestHost host, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }

    private static JsonElement Payload(string jwt)
    {
        using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }

    private sealed class Policy(McpClientRegistrationMode mode) : IMcpClientRegistrationPolicy
    {
        public McpClientRegistrationMode Mode { get; set; } = mode;

        public ValueTask<McpClientRegistrationPolicy> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new McpClientRegistrationPolicy(Mode) { RegistrationsPerHour = 100 });
    }
}
