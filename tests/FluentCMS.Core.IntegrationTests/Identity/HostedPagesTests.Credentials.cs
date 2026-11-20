using System.Net;
using System.Text.Json;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Core.Storage;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// Passwords and second factors of admin users (#3296), lockout and existing sessions (#3271), and an admin's change of a
// user with its new stamp (#3226).
public sealed partial class HostedPagesTests
{
    [Fact]
    public async Task WrongCurrentPasswordsOnChangePasswordLockTheAccount()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.5.1");
        await SignInAsync(browser, editor.Email);

        for (var attempt = 0; attempt < new AdminUserOptions().Lockout.MaxFailedAccessAttempts; attempt++)
        {
            using var wrong = await browser.SubmitAsync("/account/change-password", new Dictionary<string, string>(NewPasswordFields(NewPassword)) { ["Input.CurrentPassword"] = "not the password" });
            wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await LockedOutAsync(_host, editor)).ShouldBeTrue();

        // Locked out, the right password changes nothing either.
        using (var right = await browser.SubmitAsync("/account/change-password", new Dictionary<string, string>(NewPasswordFields(NewPassword)) { ["Input.CurrentPassword"] = Password }))
        {
            right.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        (await users.CheckPasswordAsync((await users.FindByIdAsync(editor.Id.ToString()))!, Password)).ShouldBeTrue();
    }

    // Each refusal is the replay's: the page says the code is wrong, and a fresh code passes right after it in the same
    // browser.
    [Fact]
    public async Task AnAuthenticatorCodeIsTakenOnceAtEnrolmentAndAtSignIn()
    {
        var editor = await CreateUserAsync(_host);
        var (key, _) = await EnableTwoFactorAsync(_host, editor);
        var enrolled = TotpAt(key, _host.Time.GetUtcNow().ToUnixTimeSeconds() / 30);

        // The code that turned two-factor on does not sign in.
        using (var browser = new HostedPagesBrowser(_host, "10.1.5.2"))
        {
            var twoFactor = await PasswordStepAsync(browser, editor.Email);
            await RefusedCodeAsync(browser, twoFactor, enrolled);
            using var fresh = await browser.SubmitAsync(twoFactor, new Dictionary<string, string> { ["Input.Code"] = Totp(_host, key) });
            fresh.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        // Nor does the code of a sign-in sign in a second time, nor one of an earlier step that is still in the window.
        var used = TotpAt(key, _host.Time.GetUtcNow().ToUnixTimeSeconds() / 30);
        using (var browser = new HostedPagesBrowser(_host, "10.1.5.3"))
        {
            var twoFactor = await PasswordStepAsync(browser, editor.Email);
            await RefusedCodeAsync(browser, twoFactor, used);
            await RefusedCodeAsync(browser, twoFactor, enrolled);
            browser.HasCookie(SignInCookie).ShouldBeFalse();
            using var fresh = await browser.SubmitAsync(twoFactor, new Dictionary<string, string> { ["Input.Code"] = Totp(_host, key) });
            fresh.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }
    }

    // Two sign-ins that bring the first code the user ever gives at once: one passes, the other is refused, neither fails.
    [Fact]
    public async Task TwoSignInsWithOneFirstCodeAtOncePassOnce()
    {
        var race = new FirstStepRace();
        await using var host = NewHost(new CapturedEmails())
            .Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(race.Reads, race.Saves)));
        var editor = await CreateUserAsync(host);
        string key;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            var user = (await users.FindByIdAsync(editor.Id.ToString()))!;
            (await users.ResetAuthenticatorKeyAsync(user)).Succeeded.ShouldBeTrue();
            key = (await users.GetAuthenticatorKeyAsync(user))!;
            (await users.SetTwoFactorEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
        }

        using var first = new HostedPagesBrowser(host, "10.1.5.7");
        using var second = new HostedPagesBrowser(host, "10.1.5.8");
        var firstStep = await PasswordStepAsync(first, editor.Email);
        var secondStep = await PasswordStepAsync(second, editor.Email);
        var code = Totp(host, key);

        // The first save of the step waits until both requests have read that there is none yet.
        race.Arm();
        var answers = await Task.WhenAll(
            first.SubmitAsync(firstStep, new Dictionary<string, string> { ["Input.Code"] = code }),
            second.SubmitAsync(secondStep, new Dictionary<string, string> { ["Input.Code"] = code }));
        try
        {
            answers.Select(answer => answer.StatusCode).ShouldBe([HttpStatusCode.Redirect, HttpStatusCode.OK], ignoreOrder: true);
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }
    }

    // The enable-two-factor page leaves pwd and mfa on the cookie, and a renewal of the cookie, as a password change makes,
    // keeps them both.
    [Fact]
    public async Task ACookieRenewalKeepsEveryAuthenticationMethod()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.5.9");
        await SignInAsync(browser, editor.Email);

        var key = SharedKeyOf(await browser.PageAsync("/account/enable-two-factor")).Replace(" ", "", StringComparison.Ordinal);
        using (var enabled = await browser.SubmitAsync("/account/enable-two-factor", new Dictionary<string, string> { ["Input.Code"] = Totp(_host, key) }))
        {
            enabled.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var changed = await browser.SubmitAsync("/account/change-password", new Dictionary<string, string>(NewPasswordFields(NewPassword)) { ["Input.CurrentPassword"] = Password }))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        Amr(Text(await CodeFlowAsync(_host, browser), "access_token")).ShouldBe(["pwd", "mfa"], ignoreOrder: true);
    }

    // A client with RequireConsent has its codes issued under the consent, which is no session's: a sign-in with the second
    // factor still ends that session's tokens of it issued without the factor, and the consent stays (#3228).
    [Fact]
    public async Task ASignInWithTheSecondFactorEndsTheSessionsSingleFactorTokensOfAConsentClient()
    {
        using var admin = await _host.AsUserAsync(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var editor = await CreateUserAsync(_host);
        var (key, _) = await EnableTwoFactorAsync(_host, editor);
        using var browser = new HostedPagesBrowser(_host, "10.1.5.10");
        var remembering = await PasswordStepAsync(browser, editor.Email);
        using (var remembered = await browser.SubmitAsync(remembering, new Dictionary<string, string> { ["Input.Code"] = Totp(_host, key), ["Input.RememberMachine"] = "true" }))
        {
            remembered.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var withSecondFactor = await ConsentFlowAsync(browser);
        await SignInAsync(browser, editor.Email);
        var withPassword = await ConsentFlowAsync(browser);
        var refreshed = await ThirdPartyRefreshAsync(Text(withPassword, "refresh_token"));
        (await MeAsync(_host, Text(withPassword, "access_token"))).ShouldBe(HttpStatusCode.OK);
        (await MeAsync(_host, Text(refreshed, "access_token"))).ShouldBe(HttpStatusCode.OK);

        // Neither the client nor a resource server can read the session from what they hold.
        foreach (var tokens in new[] { withPassword, refreshed })
        {
            Payload(Text(tokens, "access_token")).TryGetProperty(TokenClaims.ConsentSession, out _).ShouldBeFalse();
            Payload(Text(tokens, "id_token")).TryGetProperty(TokenClaims.ConsentSession, out _).ShouldBeFalse();
            Payload(Text(tokens, "id_token")).TryGetProperty("sid", out _).ShouldBeFalse();
        }

        // A role that requires two-factor makes the next sign-in ask for it whatever the device.
        (await AdminFixture.SendAsync(admin, HttpMethod.Put, $"users/{editor.Id:D}/roles/{await GuardedRoleAsync(admin)}")).Status.ShouldBe(HttpStatusCode.OK);
        var twoFactor = await PasswordStepAsync(browser, editor.Email);
        using (var passed = await browser.SubmitAsync(twoFactor, new Dictionary<string, string> { ["Input.Code"] = Totp(_host, key) }))
        {
            passed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        (await MeAsync(_host, Text(withPassword, "access_token"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(_host, Text(refreshed, "access_token"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await ThirdPartyRefreshRefusedAsync(Text(refreshed, "refresh_token"))).ShouldBe("invalid_grant");
        (await MeAsync(_host, Text(withSecondFactor, "access_token"))).ShouldBe(HttpStatusCode.OK);
        var next = AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid offline_access management");
        using var again = await browser.GetAsync(next.Url);
        again.Headers.Location!.AbsoluteUri.ShouldStartWith(ThirdPartyRedirectUri + "?code=");
    }

    [Fact]
    public async Task AUserWhoMustChangeThePasswordRedeemsNoRefreshToken()
    {
        using var admin = await _host.AsUserAsync(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var editor = await CreateUserAsync(_host);
        var tokens = await _host.SignInAsync(editor);

        (await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{editor.Id:D}", """{ "mustChangePassword": true }""")).Status.ShouldBe(HttpStatusCode.OK);

        (await RefusedAsync(_host, tokens.RefreshToken!)).ShouldBe("invalid_grant");
    }

    // Anyone who knows the name can lock the account out; that ends no session the user has. Deactivation still does.
    [Fact]
    public async Task ALockedOutUserKeepsItsSessionsUntilItIsDeactivated()
    {
        using var admin = await _host.AsUserAsync(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.5.5");
        await SignInAsync(browser, editor.Email);
        var tokens = await CodeFlowAsync(_host, browser);

        using (var attacker = new HostedPagesBrowser(_host, "10.1.5.6"))
        {
            for (var attempt = 0; attempt < new AdminUserOptions().Lockout.MaxFailedAccessAttempts; attempt++)
            {
                using var wrong = await attacker.SubmitAsync("/account/login", Credentials(editor.Email, "not the password"));
                wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
            }
        }

        (await LockedOutAsync(_host, editor)).ShouldBeTrue();
        var refresh = await RotatedAsync(_host, Text(tokens, "refresh_token"));
        (await MeAsync(_host, Text(await CodeFlowAsync(_host, browser), "access_token"))).ShouldBe(HttpStatusCode.OK);

        (await AdminFixture.SendAsync(admin, HttpMethod.Post, $"users/{editor.Id:D}/deactivate")).Status.ShouldBe(HttpStatusCode.OK);
        (await RefusedAsync(_host, refresh)).ShouldBe("invalid_grant");
        using var asked = await browser.GetAsync(AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri).Url);
        new Uri(OAuthBase, asked.Headers.Location!).AbsolutePath.ShouldBe("/account/login");
    }

    [Fact]
    public async Task AnAdministratorsRenameAndItsNewStampAreOneSave()
    {
        var failure = new StampSaveFailure();
        await using var host = NewHost(new CapturedEmails())
            .Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(failure)));
        using var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var editor = await CreateUserAsync(host);
        var tokens = await host.SignInAsync(editor);

        failure.UserId = editor.Id;
        var refused = await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{editor.Id:D}", """{ "userName": "kept-out@example.com" }""");
        refused.Status.ShouldNotBe(HttpStatusCode.OK);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<CmsUserManager>().FindByIdAsync(editor.Id.ToString()))!.UserName.ShouldNotBe("kept-out@example.com");
        }

        failure.UserId = null;
        var before = (await ActivityAsync(host, "user.updated", count: 0, of: row => row.TargetId == editor.Id.ToString("D"))).Count;
        var renamed = $"renamed-{Guid.NewGuid():N}@example.com";
        (await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{editor.Id:D}", $$"""{ "userName": "{{renamed}}" }""")).Status.ShouldBe(HttpStatusCode.OK);

        (await MeAsync(host, tokens.AccessToken)).ShouldBe(HttpStatusCode.Unauthorized);
        (await ActivityAsync(host, "user.updated", count: before + 1, of: row => row.TargetId == editor.Id.ToString("D"))).Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task ANameThatIsAnotherUsersAddressIsRefusedAndSoIsTheReverse()
    {
        using var admin = await _host.AsUserAsync(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var ann = await CreateUserAsync(_host);
        var bob = await CreateUserAsync(_host);

        var taken = await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{bob.Id:D}", $$"""{ "userName": "{{ann.Email.ToUpperInvariant()}}" }""");
        taken.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, taken.Body?.ToJsonString());
        taken.Body!.ToJsonString().ShouldContain("userName");

        var alias = $"alias-{Guid.NewGuid():N}@example.com";
        (await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{ann.Id:D}", $$"""{ "userName": "{{alias}}" }""")).Status.ShouldBe(HttpStatusCode.OK);
        var reverse = await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{bob.Id:D}", $$"""{ "email": "{{alias}}" }""");
        reverse.Status.ShouldBe(HttpStatusCode.Conflict, reverse.Body?.ToJsonString());
        reverse.Body!.ToJsonString().ShouldContain(IdentityErrorCodes.EmailTaken);
    }

    // The two-factor page a password sends the browser to.
    private static async Task<string> PasswordStepAsync(HostedPagesBrowser browser, string userName)
    {
        using var password = await browser.SubmitAsync("/account/login", Credentials(userName, Password));
        password.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var twoFactor = password.Headers.Location!.OriginalString;
        twoFactor.ShouldStartWith("/account/two-factor");
        return twoFactor;
    }

    private static async Task RefusedCodeAsync(HostedPagesBrowser browser, string twoFactor, string code)
    {
        using var refused = await browser.SubmitAsync(twoFactor, new Dictionary<string, string> { ["Input.Code"] = code });
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await refused.Content.ReadAsStringAsync(Token)).ShouldContain("The code is not right");
    }

    // The code flow of the third-party client, through its consent page the first time.
    private async Task<JsonElement> ConsentFlowAsync(HostedPagesBrowser browser)
    {
        var request = AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid offline_access management");
        using var asked = await browser.GetAsync(request.Url);
        var location = asked.Headers.Location!.OriginalString;
        if (!location.StartsWith("/account/consent", StringComparison.Ordinal))
        {
            return await request.ExchangeAsync(_host, asked);
        }

        using var allowed = await browser.PostAsync(location, await browser.PageAsync(location), new Dictionary<string, string> { ["decision"] = "allow" });
        using var done = await browser.FollowAsync(allowed, ThirdPartyRedirectUri);
        return await request.ExchangeAsync(_host, done);
    }

    private async Task<HttpResponseMessage> ThirdPartyRefreshResponseAsync(string refreshToken)
    {
        using var client = _host.CreateSecureClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ThirdPartyClientId,
        });
        return await client.PostAsync("/connect/token", content, Token);
    }

    private async Task<JsonElement> ThirdPartyRefreshAsync(string refreshToken)
    {
        using var response = await ThirdPartyRefreshResponseAsync(refreshToken);
        var body = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    private async Task<string?> ThirdPartyRefreshRefusedAsync(string refreshToken)
    {
        using var response = await ThirdPartyRefreshResponseAsync(refreshToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return json.RootElement.GetProperty("error").GetString();
    }

    private static async Task<bool> LockedOutAsync(FluentCmsTestHost host, TestUser user)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        return await users.IsLockedOutAsync((await users.FindByIdAsync(user.Id.ToString()))!);
    }

    // Holds the first save of an authenticator step until two requests have read the step, so that both read before either
    // writes, as two requests that cross on a real server may.
    private sealed class FirstStepRace
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<DbContext, bool> _readers = new();
        private int _armed;
        private int _saves;

        public FirstStepRace()
        {
            Reads = new ReadCounter(this);
            Saves = new SaveHolder(this);
        }

        public Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor Reads { get; }

        public Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor Saves { get; }

        public void Arm() => Volatile.Write(ref _armed, 1);

        private static bool IsStep(System.Data.Common.DbCommand command) =>
            command.Parameters.Cast<System.Data.Common.DbParameter>().Any(parameter => Equals(parameter.Value, "AuthenticatorLastStep"));

        private sealed class ReadCounter(FirstStepRace race) : DbCommandInterceptor
        {
            public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
            {
                // One request reads the step more than once, so the gate counts the requests' contexts, not the reads.
                if (Volatile.Read(ref race._armed) == 1 && eventData.Context is { } context
                    && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && IsStep(command)
                    && race._readers.TryAdd(context, true) && race._readers.Count >= 2)
                {
                    race._bothRead.TrySetResult();
                }

                return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
            }
        }

        private sealed class SaveHolder(FirstStepRace race) : SaveChangesInterceptor
        {
            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (Volatile.Read(ref race._armed) == 1
                    && eventData.Context!.ChangeTracker.Entries<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().Any(entry => entry.State == EntityState.Added && entry.Entity.Name == "AuthenticatorLastStep")
                    && Interlocked.Increment(ref race._saves) == 1)
                {
                    await race._bothRead.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                }

                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }
    }

    // Fails a save that gives the user a new security stamp.
    private sealed class StampSaveFailure : SaveChangesInterceptor
    {
        public Guid? UserId { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<CmsUser>().Where(entry => entry.Entity.Id == UserId && entry.State == EntityState.Modified).ToList())
            {
                var stored = await entry.GetDatabaseValuesAsync(cancellationToken);
                if (stored is not null && !Equals(stored[nameof(CmsUser.SecurityStamp)], entry.Entity.SecurityStamp))
                {
                    throw new DbUpdateException("The save fails for the test.");
                }
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
