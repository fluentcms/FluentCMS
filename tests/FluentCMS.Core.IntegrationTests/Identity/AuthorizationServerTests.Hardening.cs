using System.Net;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

public sealed partial class AuthorizationServerTests
{
    [Fact]
    public async Task ABearerTokenInTheQueryStringOrAFormIsNotACredential()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var tokens = await _host.SignInAsync(editor);
        using var client = _host.CreateSecureClient();

        var fromQuery = await WhoAmIAsync(client, $"?access_token={tokens.AccessToken}");
        fromQuery.GetProperty("kind").GetString().ShouldBe(nameof(PrincipalKind.Anonymous));

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["access_token"] = tokens.AccessToken });
        using var fromForm = await client.PostAsync("/api/management/v1/test/notes", form, Token);
        fromForm.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReusingARefreshTokenRevokesTheWholeChain()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var first = await _host.SignInAsync(editor);
        using var client = _host.CreateSecureClient();
        var second = await TokenAsync(client, Refresh(first.RefreshToken!), Token);

        using (var reused = await PostTokenAsync(client, Refresh(first.RefreshToken!)))
        {
            reused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var successor = await PostTokenAsync(client, Refresh(second.RefreshToken!));
        successor.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorAsync(successor)).ShouldBe("invalid_grant");

        using var api = Bearer(second.AccessToken);
        using var response = await api.GetAsync("/api/management/v1/test/whoami", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ALockedOutUserGetsNoTokenAndIsSentBackToSignIn()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var tokens = await _host.SignInAsync(editor);
        var cookie = await _host.SignInCookieAsync(editor);

        await WithUserAsync(editor, async (users, user) =>
        {
            (await users.SetLockoutEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
            (await users.SetLockoutEndDateAsync(user, _host.Time.GetUtcNow().AddHours(1))).Succeeded.ShouldBeTrue();
        });

        using var client = _host.CreateSecureClient();
        using var refreshed = await PostTokenAsync(client, Refresh(tokens.RefreshToken!));
        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorAsync(refreshed)).ShouldBe("invalid_grant");

        using var authorize = new HttpRequestMessage(HttpMethod.Get, AuthorizePath());
        authorize.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(authorize, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.ShouldBe("/account/login");
    }

    [Fact]
    public async Task APasswordChangeEndsTheRefreshToken()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var tokens = await _host.SignInAsync(editor);
        using var client = _host.CreateSecureClient();
        var rotated = await TokenAsync(client, Refresh(tokens.RefreshToken!), Token);

        await WithUserAsync(editor, async (users, user) => (await users.AddPasswordAsync(user, "A-new-password-1")).Succeeded.ShouldBeTrue());

        using var refreshed = await PostTokenAsync(client, Refresh(rotated.RefreshToken!));
        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorAsync(refreshed)).ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task AClientDroppedFromTheOptionsIsDeletedWithItsTokensAndOthersStay()
    {
        var directory = Directory.CreateTempSubdirectory("fluentcms-clients-");
        try
        {
            var file = Path.Combine(directory.FullName, "cms.db");
            TestTokens issued;
            await using (var first = NewHost().UseSqlite(file))
            {
                using var client = first.CreateSecureClient();
                issued = await TokenAsync(client, ClientCredentials("management"), Token);

                await using var scope = first.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(
                    new OpenIddictApplicationDescriptor { ClientId = "registered-elsewhere", ClientType = OpenIddictConstants.ClientTypes.Public },
                    Token);
            }

            var logs = new LogCapture();
            await using var second = NewHost(withServiceClient: false).UseSqlite(file).WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)));
            _ = second.Services;
            var deleted = logs.Entries.Where(entry => entry.EventId.Id == OidcLog.ClientDeletedId).ShouldHaveSingleItem();
            deleted.Level.ShouldBe(LogLevel.Warning);
            deleted.State["ClientId"].ShouldBe(ServiceClientId);

            using (var client = second.CreateSecureClient())
            {
                using var refused = await PostTokenAsync(client, ClientCredentials("management"));
                refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
                (await ErrorAsync(refused)).ShouldBe("invalid_client");
            }

            using (var api = Bearer(issued.AccessToken, second))
            {
                using var response = await api.GetAsync("/api/management/v1/test/whoami", Token);
                response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            await using var check = second.Services.CreateAsyncScope();
            var applications = check.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            (await applications.FindByClientIdAsync(ServiceClientId, Token)).ShouldBeNull();
            (await applications.FindByClientIdAsync("registered-elsewhere", Token)).ShouldNotBeNull();
            (await check.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OpenIddictEntityFrameworkCoreToken<Guid>>().CountAsync(Token)).ShouldBe(0);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AnEmptyClientListDeletesNothingAndSaysWhatItKept()
    {
        var directory = Directory.CreateTempSubdirectory("fluentcms-clients-");
        try
        {
            var file = Path.Combine(directory.FullName, "cms.db");
            await using (var first = NewHost().UseSqlite(file))
            {
                _ = first.Services;
            }

            var logs = new LogCapture();
            await using var second = NewHost(withServiceClient: false)
                .UseSqlite(file)
                .Configure(cms => cms.Clients.Applications.Clear())
                .WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)));
            _ = second.Services;

            logs.Entries.ShouldNotContain(entry => entry.EventId.Id == OidcLog.ClientDeletedId);
            var kept = logs.Entries.Where(entry => entry.EventId.Id == OidcLog.ClientsKeptId).ShouldHaveSingleItem();
            kept.Level.ShouldBe(LogLevel.Warning);
            ((string)kept.State["ClientIds"]!).Split(", ").ShouldBe([ServiceClientId, FluentCmsTestHost.OAuthClientId], ignoreOrder: true);

            using var client = second.CreateSecureClient();
            (await TokenAsync(client, ClientCredentials("management"), Token)).AccessToken.ShouldNotBeNullOrEmpty();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TheDataProtectionKeyRingIsMadeOnFirstUseNotAtStart()
    {
        _ = _host.Services;
        DataProtectionKeys(_host).ShouldBe(0);

        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await _host.SignInAsync(editor);

        DataProtectionKeys(_host).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ThePruneJobRemovesEndedSessionsAndTheirTokens()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var kept = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await _host.SignInAsync(editor);
        await _host.SignInAsync(kept);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<UserSessions>();
            var session = (await sessions.ListAsync(editor.Id, Token)).ShouldHaveSingleItem();
            (await sessions.RevokeAsync(editor.Id, session.Id, Token)).ShouldBeTrue();
        }

        _host.Time.Advance(OidcPruneJob.MinimumAge + TimeSpan.FromDays(1));
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var operation = new OperationContext(Principal.System, Origin.Job, new SiteId(DefaultSite.Id), DefaultSite.Locale, null, "prune", _host.Time.GetUtcNow());
            await scope.ServiceProvider.GetRequiredService<OidcPruneJob>().ExecuteAsync(
                new OidcPrunePayload(),
                new JobContext(Guid.CreateVersion7(), OidcPruneJob.Type, "default", 1, 3, operation, Token));
        }

        await using var check = _host.Services.CreateAsyncScope();
        var context = check.ServiceProvider.GetRequiredService<SystemDbContext>();
        var subject = editor.Id.ToString("D");
        (await context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().CountAsync(row => row.Subject == subject, Token)).ShouldBe(0);
        (await context.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().CountAsync(row => row.Subject == subject, Token)).ShouldBe(0);
        (await context.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().CountAsync(row => row.Subject == kept.Id.ToString("D"), Token)).ShouldBe(1);
    }

    [Fact]
    public async Task ASessionWhoseTokensExpiredIsNotListed()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await _host.SignInAsync(editor);

        await using var scope = _host.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<UserSessions>();
        (await sessions.ListAsync(editor.Id, Token)).ShouldHaveSingleItem();

        // Past the refresh token's lifetime, the longest of the session's tokens.
        _host.Time.Advance(TimeSpan.FromDays(15));
        (await sessions.ListAsync(editor.Id, Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task InstancesThatStartTogetherAgreeOnOneSigningKey()
    {
        var directory = Directory.CreateTempSubdirectory("fluentcms-keys-");
        try
        {
            var file = Path.Combine(directory.FullName, "cms.db");
            await using var first = NewHost().UseSqlite(file);
            _ = first.Services;
            await using var second = NewHost().UseSqlite(file);
            _ = second.Services;

            var stores = new[] { first, second, first, second }.Select(host => host.Services.GetRequiredService<SigningKeyStore>()).ToArray();
            var keys = await Task.WhenAll(stores.Select(store => Task.Run(() => store.Keys(SigningKey.Signing).Single().KeyId)));

            keys.Distinct().ShouldHaveSingleItem();
            SigningKeys(first).Count(key => key.Use == SigningKey.Signing).ShouldBe(1);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LogoutByGetNeedsTheSessionsIdTokenHint()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var other = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var tokens = await _host.SignInAsync(editor, "openid", "management");
        var othersTokens = await _host.SignInAsync(other, "openid", "management");
        var cookie = await _host.SignInCookieAsync(editor);
        using var client = _host.CreateSecureClient();

        using (var bare = await SendAsync(client, HttpMethod.Get, "/connect/logout", cookie))
        {
            bare.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ErrorAsync(bare)).ShouldBe("invalid_request");
            bare.Headers.Contains("Set-Cookie").ShouldBeFalse();
        }

        using (var forged = await SendAsync(client, HttpMethod.Get, "/connect/logout?id_token_hint=not.a.token", cookie))
        {
            forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            forged.Headers.Contains("Set-Cookie").ShouldBeFalse();
        }

        using (var someoneElse = await SendAsync(client, HttpMethod.Get, $"/connect/logout?id_token_hint={othersTokens.IdentityToken}", cookie))
        {
            someoneElse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var hinted = await SendAsync(client, HttpMethod.Get, $"/connect/logout?id_token_hint={tokens.IdentityToken}", cookie))
        {
            hinted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            hinted.Headers.GetValues("Set-Cookie").ShouldContain(value => value.StartsWith(".AspNetCore.Identity.Application=;", StringComparison.Ordinal));
        }

        using var posted = await SendAsync(client, HttpMethod.Post, "/connect/logout", cookie, new FormUrlEncodedContent([]));
        posted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task UnderAPathBaseTheServerAnswersThereAndItsUrlsCarryIt()
    {
        await using var host = NewHost().Configure(cms => cms.ReverseProxy.PathBase = "/cms");
        using var client = host.CreateSecureClient();

        using (var discovery = await client.GetAsync("/cms/.well-known/openid-configuration", Token))
        {
            discovery.StatusCode.ShouldBe(HttpStatusCode.OK);
            var document = await JsonAsync(discovery);
            document.GetProperty("issuer").GetString().ShouldBe("https://localhost/cms");
            document.GetProperty("token_endpoint").GetString().ShouldBe("https://localhost/cms/connect/token");
            document.GetProperty("jwks_uri").GetString().ShouldBe("https://localhost/cms/.well-known/jwks");
        }

        using (var outside = await client.GetAsync("/.well-known/openid-configuration", Token))
        {
            outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var content = new FormUrlEncodedContent(ClientCredentials("management")))
        using (var outside = await client.PostAsync("/connect/token", content, Token))
        {
            outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var form = new FormUrlEncodedContent(ClientCredentials("management"));
        using var issued = await client.PostAsync("/cms/connect/token", form, Token);
        issued.StatusCode.ShouldBe(HttpStatusCode.OK);
        var accessToken = (await JsonAsync(issued)).GetProperty("access_token").GetString()!;
        Payload(accessToken).GetProperty("iss").GetString().ShouldBe("https://localhost/cms");

        using var api = Bearer(accessToken, host);
        using var caller = await api.GetAsync("/cms/api/management/v1/test/whoami", Token);
        caller.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(caller)).GetProperty("kind").GetString().ShouldBe(nameof(PrincipalKind.ServiceAccount));
    }

    [Fact]
    public async Task TheDataProtectionStatusSaysWhetherTheKeyRingIsEncrypted()
    {
        _host.Services.GetRequiredService<IDataProtectionStatus>().KeysEncrypted.ShouldBeFalse();

        await using var encrypted = NewHost().WithServices(services =>
            services.Configure<KeyManagementOptions>(options => options.XmlEncryptor = new NullXmlEncryptor()));
        encrypted.Services.GetRequiredService<IDataProtectionStatus>().KeysEncrypted.ShouldBeTrue();
    }

    private static System.Text.Json.JsonElement Payload(string jwt)
    {
        using var document = System.Text.Json.JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }

    private async Task WithUserAsync(TestUser user, Func<UserManager<CmsUser>, CmsUser, Task> change)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<CmsUser>>();
        var stored = await users.FindByIdAsync(user.Id.ToString()) ?? throw new ShouldAssertException("The user is gone.");
        await change(users, stored);
    }

    private static string AuthorizePath() =>
        $"/connect/authorize?client_id={FluentCmsTestHost.OAuthClientId}&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString(FluentCmsTestHost.OAuthRedirectUri)}&code_challenge=abcdefghijabcdefghijabcdefghijabcdefghijabc&code_challenge_method=S256";

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string cookie, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request, Token);
    }

    private static async Task<System.Text.Json.JsonElement> WhoAmIAsync(HttpClient api, string query)
    {
        using var response = await api.GetAsync("/api/management/v1/test/whoami" + query, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return await JsonAsync(response);
    }
}
