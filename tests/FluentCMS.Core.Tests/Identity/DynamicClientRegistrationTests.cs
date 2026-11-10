using System.Collections.Immutable;
using System.Text.Json;
using FluentCMS.Agents;
using FluentCMS.Core.Identity.Oidc;
using Shouldly;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Tests.Identity;

// What a client may send to /connect/register (RFC 7591), and what its registration is stored as.
public sealed class DynamicClientRegistrationTests
{
    [Theory]
    [InlineData("https://app.example.com/callback")]
    [InlineData("https://app.example.com:8443/callback?tenant=a")]
    [InlineData("http://localhost:33418/callback")]
    [InlineData("http://LOCALHOST/callback")]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://[::1]:8080/callback")]
    public void ARedirectUriIsHttpsOrHttpOnLoopback(string uri)
    {
        DynamicClientRegistration.RedirectUri(uri).ShouldNotBeNull().OriginalString.ShouldBe(uri);
    }

    [Theory]
    [InlineData("http://app.example.com/callback")]
    [InlineData("http://localhost.example.com/callback")]
    [InlineData("http://127.0.0.1.example.com/callback")]
    [InlineData("http://192.168.1.10/callback")]
    [InlineData("http://0.0.0.0/callback")]
    [InlineData("https://*.example.com/callback")]
    [InlineData("https://app.example.com/*")]
    [InlineData("https://app.example.com/callback#fragment")]
    [InlineData("https://user@app.example.com/callback")]
    [InlineData("https://user:secret@app.example.com/callback")]
    [InlineData("http://localhost@evil.example.com/callback")]
    [InlineData("cursor://callback")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("/callback")]
    [InlineData("app.example.com/callback")]
    [InlineData("https://app.example.com/call back")]
    [InlineData("https://")]
    [InlineData("")]
    public void AnyOtherRedirectUriIsRefused(string uri)
    {
        DynamicClientRegistration.RedirectUri(uri).ShouldBeNull();
    }

    [Fact]
    public void ARedirectUriLongerThanTheLimitIsRefused()
    {
        DynamicClientRegistration.RedirectUri("https://app.example.com/" + new string('a', DynamicClientRegistration.MaxRedirectUriLength)).ShouldBeNull();
    }

    [Fact]
    public void ARegistrationWithRedirectUrisAloneGetsTheDefaults()
    {
        var (metadata, error, _) = Parse("""{ "redirect_uris": ["https://app.example.com/cb", "https://app.example.com/cb"] }""");

        error.ShouldBeNull();
        metadata.ShouldNotBeNull();
        metadata.Name.ShouldBe(DynamicClientRegistration.DefaultName);
        metadata.RedirectUris.Select(uri => uri.OriginalString).ShouldBe(["https://app.example.com/cb"]);
        metadata.GrantTypes.ShouldBe([GrantTypes.AuthorizationCode, GrantTypes.RefreshToken]);
        metadata.Scopes.ShouldBe(["mcp", "openid", "profile", "email", "offline_access"]);
    }

    [Fact]
    public void ARegistrationKeepsWhatItAskedForAndIgnoresWhatTheServerHasNoUseFor()
    {
        var (metadata, error, _) = Parse("""
            {
              "redirect_uris": ["http://127.0.0.1:7777/cb"],
              "client_name": "  Editor  ",
              "grant_types": ["authorization_code"],
              "response_types": ["code"],
              "token_endpoint_auth_method": "none",
              "scope": "openid mcp",
              "client_uri": "https://app.example.com",
              "logo_uri": "https://app.example.com/logo.png",
              "software_id": "editor"
            }
            """);

        error.ShouldBeNull();
        metadata.ShouldNotBeNull();
        metadata.Name.ShouldBe("Editor");
        metadata.GrantTypes.ShouldBe([GrantTypes.AuthorizationCode]);
        metadata.Scopes.ShouldBe(["mcp", "openid"]);
    }

    [Theory]
    [InlineData("""[]""", "invalid_client_metadata")]
    [InlineData("""{ }""", "invalid_redirect_uri")]
    [InlineData("""{ "redirect_uris": [] }""", "invalid_redirect_uri")]
    [InlineData("""{ "redirect_uris": "https://app.example.com/cb" }""", "invalid_redirect_uri")]
    [InlineData("""{ "redirect_uris": [42] }""", "invalid_redirect_uri")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb", "http://app.example.com/cb"] }""", "invalid_redirect_uri")]
    [InlineData("""{ "redirect_uris": ["https://a/1","https://a/2","https://a/3","https://a/4","https://a/5","https://a/6","https://a/7","https://a/8","https://a/9","https://a/10","https://a/11"] }""", "invalid_redirect_uri")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "token_endpoint_auth_method": "client_secret_basic" }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "token_endpoint_auth_method": "client_secret_post" }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "token_endpoint_auth_method": "private_key_jwt" }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "token_endpoint_auth_method": null }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "grant_types": ["authorization_code", "client_credentials"] }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "grant_types": ["implicit"] }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "grant_types": ["refresh_token"] }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "grant_types": "authorization_code" }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "response_types": ["token"] }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "response_types": ["code", "id_token"] }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "response_types": [] }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "scope": "mcp management" }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "scope": "draft" }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "scope": ["mcp"] }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "client_name": "" }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "client_name": "line\nbreak" }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://app.example.com/cb"], "client_name": 7 }""", "invalid_client_metadata")]
    public void ARegistrationThatAsksForMoreThanAPublicMcpClientGetsIsRefused(string body, string expected)
    {
        var (metadata, error, description) = Parse(body);

        metadata.ShouldBeNull();
        error.ShouldBe(expected);
        description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ANameLongerThanTheLimitIsRefused()
    {
        var (metadata, error, _) = Parse($$"""{ "redirect_uris": ["https://app.example.com/cb"], "client_name": "{{new string('n', DynamicClientRegistration.MaxNameLength + 1)}}" }""");

        metadata.ShouldBeNull();
        error.ShouldBe("invalid_client_metadata");
    }

    [Fact]
    public void TheClientIsPublicWithPkceConsentAndOnlyTheScopesOfARegisteredClient()
    {
        var metadata = new ClientMetadata("Editor", [new Uri("https://app.example.com/cb")], [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken], ["mcp", "openid", "offline_access", "management"]);

        var descriptor = McpClients.Describe("mcp-1", metadata);

        descriptor.ClientType.ShouldBe(ClientTypes.Public);
        descriptor.ClientSecret.ShouldBeNull();
        descriptor.ConsentType.ShouldBe(ConsentTypes.Explicit);
        descriptor.Requirements.ShouldContain(Requirements.Features.ProofKeyForCodeExchange);
        descriptor.Permissions.ShouldBe(
            [
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.Endpoints.EndSession,
                Permissions.ResponseTypes.Code,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Prefixes.Scope + "mcp",
                Permissions.Prefixes.Scope + "offline_access",
            ],
            ignoreOrder: true);
        descriptor.RedirectUris.ShouldHaveSingleItem().OriginalString.ShouldBe("https://app.example.com/cb");
    }

    [Fact]
    public void TheStateOfAClientSurvivesItsProperty()
    {
        var at = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var role = Guid.CreateVersion7();
        var approver = Guid.CreateVersion7();
        var state = new McpClientState(McpClientStatus.Approved, at) { RoleId = role, ApprovedBy = approver, ApprovedAt = at.AddHours(1) };

        var read = McpClientState.Of(Properties(state.ToProperty()));

        read.ShouldBe(state);
        read!.IsApproved.ShouldBeTrue();
        state.ToProperty().GetProperty("status").GetString().ShouldBe("approved");
    }

    [Theory]
    [InlineData("""{ "status": "approved", "registeredAt": "2026-11-10T09:00:00+00:00" }""")]
    [InlineData("""{ "status": "unknown" }""")]
    [InlineData("""{ "status": 1, "roleId": "0198c7b2-4a39-7c11-8d55-4f6c5a0f4f10" }""")]
    [InlineData("""true""")]
    [InlineData("""{ }""")]
    public void AStateThatIsDamagedOrApprovedWithoutARoleCountsAsPending(string property)
    {
        using var document = JsonDocument.Parse(property);

        var state = McpClientState.Of(Properties(document.RootElement.Clone()));

        state.ShouldNotBeNull();
        state.Status.ShouldBe(McpClientStatus.Pending);
        state.IsApproved.ShouldBeFalse();
    }

    [Fact]
    public void AClientThatDidNotRegisterItselfHasNoState()
    {
        McpClientState.Of(ImmutableDictionary<string, JsonElement>.Empty).ShouldBeNull();
    }

    [Fact]
    public void AnAddressRegistersItsLimitInAnHourAndAnotherAddressIsCountedApart()
    {
        var time = new Clock(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        var limiter = new ClientRegistrationLimiter(time);

        limiter.TryAcquire("ip:10.0.0.1", 2, out _).ShouldBeTrue();
        time.Advance(TimeSpan.FromMinutes(10));
        limiter.TryAcquire("ip:10.0.0.1", 2, out _).ShouldBeTrue();
        limiter.TryAcquire("ip:10.0.0.1", 2, out var retryAfter).ShouldBeFalse();
        retryAfter.ShouldBe(TimeSpan.FromMinutes(50));
        limiter.TryAcquire("ip:10.0.0.2", 2, out _).ShouldBeTrue();

        // The first registration leaves the window, which frees one place.
        time.Advance(TimeSpan.FromMinutes(50));
        limiter.TryAcquire("ip:10.0.0.1", 2, out _).ShouldBeTrue();
        limiter.TryAcquire("ip:10.0.0.1", 2, out _).ShouldBeFalse();
    }

    private static (ClientMetadata? Metadata, string? Error, string? Description) Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return DynamicClientRegistration.Parse(document.RootElement);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private static ImmutableDictionary<string, JsonElement> Properties(JsonElement state) =>
        ImmutableDictionary<string, JsonElement>.Empty.Add(AuthorizationServer.McpClientProperty, state);
}
