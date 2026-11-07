using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.IntegrationTests.Testing;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

public sealed partial class AuthorizationServerTests : IAsyncDisposable
{
    private const string TestClientId = FluentCmsTestHost.OAuthClientId;
    private const string ServiceClientId = "billing";
    private const string ServiceClientSecret = "test-secret-of-the-billing-service";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _host = NewHost();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task APublicClientGetsATokenWithPkceAndItsRefreshTokenRotates()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

        var first = await _host.SignInAsync(editor, "openid", "offline_access", "management");

        Header(first.AccessToken).GetProperty("alg").GetString().ShouldBe("RS256");
        first.RefreshToken.ShouldNotBeNull();
        first.IdentityToken.ShouldNotBeNull();

        using var client = _host.CreateSecureClient();
        var second = await TokenAsync(client, Refresh(first.RefreshToken), Token);
        second.RefreshToken.ShouldNotBeNull();
        second.RefreshToken.ShouldNotBe(first.RefreshToken);

        _host.Time.Advance(TimeSpan.FromMinutes(1));
        using var reused = await PostTokenAsync(client, Refresh(first.RefreshToken));
        reused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorAsync(reused)).ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task AnAuthorizationRequestWithoutPkceIsRefused()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var client = _host.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/connect/authorize?client_id={FluentCmsTestHost.OAuthClientId}&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString(FluentCmsTestHost.OAuthRedirectUri)}");
        request.Headers.Add("Cookie", await _host.SignInCookieAsync(editor));

        using var response = await client.SendAsync(request, Token);

        // Refused before the client's redirect URI is trusted, so the error page shows it instead of the client.
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = new Uri(client.BaseAddress!, response.Headers.Location!);
        location.AbsolutePath.ShouldBe("/account/error");
        var error = QueryHelpers.ParseQuery(location.Query);
        error["error"].ToString().ShouldBe("invalid_request");
        error["error_description"].ToString().ShouldContain("'code_challenge'");
    }

    [Fact]
    public async Task AnUnauthenticatedUserIsSentToTheHostedLoginPage()
    {
        using var client = _host.CreateSecureClient();

        using var response = await client.GetAsync($"/connect/authorize?client_id={FluentCmsTestHost.OAuthClientId}&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString(FluentCmsTestHost.OAuthRedirectUri)}&code_challenge=abcdefghijabcdefghijabcdefghijabcdefghijabc&code_challenge_method=S256", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.ShouldBe("/account/login");
        QueryHelpers.ParseQuery(response.Headers.Location.Query)["ReturnUrl"].ToString().ShouldStartWith("/connect/authorize?");
    }

    [Fact]
    public async Task AConfidentialClientGetsATokenWithClientCredentials()
    {
        using var client = _host.CreateSecureClient();
        var tokens = await TokenAsync(client, ClientCredentials("management"), Token);
        tokens.RefreshToken.ShouldBeNull();

        using var api = Bearer(tokens.AccessToken);
        var caller = await WhoAmIAsync(api);
        caller.GetProperty("kind").GetString().ShouldBe(nameof(PrincipalKind.ServiceAccount));
        caller.GetProperty("clientId").GetString().ShouldBe(ServiceClientId);
        caller.GetProperty("origin").GetString().ShouldBe(nameof(Origin.Api));

        // Its Editor role comes from the client's registration.
        using var created = await api.PostAsync("/api/management/v1/test/notes", content: null, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AWrongClientSecretGetsNoToken()
    {
        using var client = _host.CreateSecureClient();
        var form = new Dictionary<string, string>(ClientCredentials("management")) { ["client_secret"] = "not-the-secret" };

        using var response = await PostTokenAsync(client, form);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ErrorAsync(response)).ShouldBe("invalid_client");
    }

    [Fact]
    public async Task AGeneratedSigningKeyIsStoredAndReusedAfterARestart()
    {
        var directory = Directory.CreateTempSubdirectory("fluentcms-oidc-");
        try
        {
            var file = Path.Combine(directory.FullName, "cms.db");
            TestUser editor;
            TestTokens tokens;
            int dataProtectionKeys;
            await using (var first = NewHost().UseSqlite(file))
            {
                editor = await first.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
                tokens = await first.SignInAsync(editor);

                var keys = SigningKeys(first);
                keys.Count(key => key.Use == SigningKey.Signing).ShouldBe(1);
                keys.Count(key => key.Use == SigningKey.Encryption).ShouldBe(1);
                keys.ShouldAllBe(key => !key.ProtectedKey.Contains("PRIVATE", StringComparison.Ordinal));
                Header(tokens.AccessToken).GetProperty("kid").GetString().ShouldBe(keys.Single(key => key.Use == SigningKey.Signing).Id.ToString("N"));

                dataProtectionKeys = DataProtectionKeys(first);
                dataProtectionKeys.ShouldBeGreaterThan(0);
            }

            await using var second = NewHost().UseSqlite(file);

            using (var api = Bearer(tokens.AccessToken, second))
            {
                (await WhoAmIAsync(api)).GetProperty("id").GetGuid().ShouldBe(editor.Id);
            }

            using var client = second.CreateSecureClient();
            (await TokenAsync(client, Refresh(tokens.RefreshToken!), Token)).AccessToken.ShouldNotBeNullOrEmpty();

            SigningKeys(second).Count.ShouldBe(2);
            DataProtectionKeys(second).ShouldBe(dataProtectionKeys);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AConfiguredCertificateSignsTheTokensInsteadOfAGeneratedKey()
    {
        var directory = Directory.CreateTempSubdirectory("fluentcms-certificate-");
        try
        {
            using var rsa = RSA.Create(2048);
            using var certificate = new CertificateRequest("CN=FluentCMS test signing", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            var path = Path.Combine(directory.FullName, "signing.pfx");
            await File.WriteAllBytesAsync(path, certificate.Export(X509ContentType.Pkcs12, "test-password"), Token);

            await using var host = NewHost().WithServices(services => services.Configure<SecurityOptions>(security =>
            {
                security.SigningCertificate.Path = path;
                security.SigningCertificate.Password = "test-password";
            }));
            var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

            var tokens = await host.SignInAsync(editor);

            host.Services.GetRequiredService<ISigningKeyStatus>().IsGenerated.ShouldBeFalse();
            Header(tokens.AccessToken).GetProperty("kid").GetString().ShouldBe(certificate.Thumbprint);
            SigningKeys(host).ShouldAllBe(key => key.Use == SigningKey.Encryption);
            using var api = Bearer(tokens.AccessToken, host);
            (await WhoAmIAsync(api)).GetProperty("id").GetGuid().ShouldBe(editor.Id);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AGeneratedKeyIsReportedForTheProductionCheck()
    {
        _host.Services.GetRequiredService<ISigningKeyStatus>().IsGenerated.ShouldBeTrue();
    }

    [Fact]
    public async Task ABearerTokenCarriesTheUsersRolesAndItsClientLandsOnTheVersionAndTheActivityRow()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await _host.CreateRoleAsync("Nobody", [], Token);
        var nobody = await _host.CreateUserAsync("Nobody", cancellationToken: Token);

        using (var refused = await _host.AsUserAsync(nobody))
        {
            using var response = await refused.PostAsync("/api/management/v1/test/notes", content: null, Token);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using var api = await _host.AsUserAsync(editor);
        var caller = await WhoAmIAsync(api);
        caller.GetProperty("kind").GetString().ShouldBe(nameof(PrincipalKind.AdminUser));
        caller.GetProperty("id").GetGuid().ShouldBe(editor.Id);
        caller.GetProperty("clientId").GetString().ShouldBe(FluentCmsTestHost.OAuthClientId);
        caller.GetProperty("scope").GetString()!.Split(' ').ShouldContain(TokenScopes.Management);

        using var created = await api.PostAsync("/api/management/v1/test/notes", content: null, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();

        await using var scope = _host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var version = await context.Set<ContentVersion>().AsNoTracking().SingleAsync(version => version.DocumentId == id, Token);
        version.ClientId.ShouldBe(FluentCmsTestHost.OAuthClientId);
        version.Origin.ShouldBe(Origin.Api);
        version.AuthorId.ShouldBe(editor.Id);

        var activity = await ActivityAsync(_host, editor.Id);
        activity.ClientId.ShouldBe(FluentCmsTestHost.OAuthClientId);
        activity.Origin.ShouldBe(Origin.Api);
    }

    [Fact]
    public async Task TheAdminClientRunsWithTheAdminOrigin()
    {
        await using var host = NewHost().Configure(cms => cms.Clients.AdminClientId = FluentCmsTestHost.OAuthClientId);
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var api = await host.AsUserAsync(editor);

        (await WhoAmIAsync(api)).GetProperty("origin").GetString().ShouldBe(nameof(Origin.Admin));

        using var created = await api.PostAsync("/api/management/v1/test/notes", content: null, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ActivityAsync(host, editor.Id)).Origin.ShouldBe(Origin.Admin);
    }

    [Fact]
    public async Task RevokingASessionInvalidatesItsRefreshToken()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var tokens = await _host.SignInAsync(editor);

        UserSession session;
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<UserSessions>();
            session = (await sessions.ListAsync(editor.Id, Token)).ShouldHaveSingleItem();
            session.ClientId.ShouldBe(FluentCmsTestHost.OAuthClientId);
            session.Scopes.ShouldContain(TokenScopes.Management);

            (await sessions.RevokeAsync(Guid.CreateVersion7(), session.Id, Token)).ShouldBeFalse();
            (await sessions.RevokeAsync(editor.Id, session.Id, Token)).ShouldBeTrue();
            (await sessions.ListAsync(editor.Id, Token)).ShouldBeEmpty();
        }

        using var client = _host.CreateSecureClient();
        using var refreshed = await PostTokenAsync(client, Refresh(tokens.RefreshToken!));
        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorAsync(refreshed)).ShouldBe("invalid_grant");

        using var api = Bearer(tokens.AccessToken);
        using var response = await api.GetAsync("/api/management/v1/test/whoami", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenWithoutTheDraftScopeCannotReadDraftsThroughDelivery()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

        using (var management = await _host.AsUserAsync(editor, "openid", "management"))
        {
            using var response = await management.GetAsync("/api/delivery/v1/note?status=draft", Token);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await JsonAsync(response)).GetProperty("code").GetString().ShouldBe(QueryErrorCodes.DraftNotAllowed);
        }

        using var draft = await _host.AsUserAsync(editor, "openid", "draft");
        using var drafts = await draft.GetAsync("/api/delivery/v1/note?status=draft", Token);
        drafts.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnApiKeyAndABearerTokenInOneRequestAreRefused()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var key = await _host.CreateApiKeyAsync(FluentCMS.Testing.ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        using var api = await _host.AsUserAsync(editor);
        api.DefaultRequestHeaders.Add(ApiKeyAuthentication.Header, key.Secret);

        using var response = await api.GetAsync("/api/management/v1/test/whoami", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await JsonAsync(response)).GetProperty("code").GetString().ShouldBe(ApiKeyErrorCodes.SeveralCredentials);
    }

    [Fact]
    public async Task TheFluentCmsSchemeIgnoresTheSignInCookie()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var client = _host.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/management/v1/test/whoami");
        request.Headers.Add("Cookie", await _host.SignInCookieAsync(editor));

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("kind").GetString().ShouldBe(nameof(PrincipalKind.Anonymous));
    }

    [Fact]
    public async Task TheDiscoveryDocumentNamesTheEndpointsAndCarriesTheSecurityHeaders()
    {
        using var client = _host.CreateSecureClient();

        using var response = await client.GetAsync("/.well-known/openid-configuration", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        var document = await JsonAsync(response);
        document.GetProperty("token_endpoint").GetString().ShouldBe("https://localhost/connect/token");
        document.GetProperty("authorization_endpoint").GetString().ShouldBe("https://localhost/connect/authorize");
        document.GetProperty("end_session_endpoint").GetString().ShouldBe("https://localhost/connect/logout");
        document.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(method => method.GetString()).ShouldBe(["S256"]);
        document.GetProperty("scopes_supported").EnumerateArray().Select(scope => scope.GetString()).ShouldContain(TokenScopes.Draft);
    }

    [Fact]
    public async Task TheUserInfoEndpointAnswersWithTheScopesClaims()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var api = await _host.AsUserAsync(editor, "openid", "profile", "email");

        using var response = await api.GetAsync("/connect/userinfo", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var claims = await JsonAsync(response);
        claims.GetProperty("sub").GetGuid().ShouldBe(editor.Id);
        claims.GetProperty("email").GetString().ShouldBe(editor.Email);
    }

    private static FluentCmsTestHost NewHost(bool withServiceClient = true) => new FluentCmsTestHost()
        .WithContentTypes<Note>()
        .Configure(cms =>
        {
            if (withServiceClient)
            {
                cms.Clients.Applications[ServiceClientId] = new ClientRegistration
                {
                    DisplayName = "Billing service",
                    Secret = ServiceClientSecret,
                    Scopes = { TokenScopes.Management },
                    Roles = { BuiltInRoles.EditorName },
                };
            }
        })
        .WithServices(services => services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, group =>
        {
            group.MapGet("test/whoami", (HttpContext http) =>
            {
                var operation = ApiRequest.Of(http).Operation;
                return Results.Json(new
                {
                    kind = operation.Principal.Kind.ToString(),
                    id = operation.Principal.Id,
                    origin = operation.Origin.ToString(),
                    clientId = operation.ClientId,
                    scope = operation.Principal.Claim(TokenScopes.Claim),
                });
            }).WithFluentCmsOperation(Origin.Api);

            group.MapPost("test/notes", async ([FromServices] IContentService<Note> notes, CancellationToken cancellationToken) =>
            {
                var note = await notes.CreateAsync(new Note { Title = "By bearer" }, cancellationToken);
                return Results.Json(new { id = note.Id });
            }).WithFluentCmsOperation(Origin.Api);
        }));

    private HttpClient Bearer(string accessToken, FluentCmsTestHost? host = null)
    {
        var client = (host ?? _host).CreateSecureClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static Dictionary<string, string> Refresh(string refreshToken) => new()
    {
        ["grant_type"] = "refresh_token",
        ["refresh_token"] = refreshToken,
        ["client_id"] = TestClientId,
    };

    private static Dictionary<string, string> ClientCredentials(string scope) => new()
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = ServiceClientId,
        ["client_secret"] = ServiceClientSecret,
        ["scope"] = scope,
    };

    private static async Task<HttpResponseMessage> PostTokenAsync(HttpClient client, Dictionary<string, string> form)
    {
        using var content = new FormUrlEncodedContent(form);
        return await client.PostAsync("/connect/token", content, Token);
    }

    private static async Task<TestTokens> TokenAsync(HttpClient client, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await PostTokenAsync(client, form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        var json = await JsonAsync(response);
        return new TestTokens(
            json.GetProperty("access_token").GetString()!,
            json.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            json.TryGetProperty("id_token", out var identity) ? identity.GetString() : null);
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("error").GetString();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> WhoAmIAsync(HttpClient api)
    {
        using var response = await api.GetAsync("/api/management/v1/test/whoami", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return await JsonAsync(response);
    }

    private static JsonElement Header(string jwt)
    {
        using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Split('.')[0]));
        return document.RootElement.Clone();
    }

    private static List<SigningKey> SigningKeys(FluentCmsTestHost host)
    {
        using var scope = host.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<SigningKey>().AsNoTracking()];
    }

    private static int DataProtectionKeys(FluentCmsTestHost host)
    {
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<DataProtectionKey>().Count();
    }

    private static async Task<ActivityRow> ActivityAsync(FluentCmsTestHost host, Guid actor)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
            await using var scope = host.Services.CreateAsyncScope();
            var row = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ActivityRow>().AsNoTracking()
                .FirstOrDefaultAsync(row => row.ActorId == actor, Token);
            if (row is not null)
            {
                return row;
            }

            await Task.Delay(100, Token);
        }

        throw new ShouldAssertException("No activity row was written.");
    }
}
