using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// The follow-ups of #3363: a recent sign-in before two-factor is set up through /me (#3372), the steps of a signed-in user
// that the lockout no longer refuses (#3371), the forced password change of a user without a password (#3378), and what the
// invitation page tells of the users' addresses (#3373). Each on a host of its own where it moves the clock.
public sealed partial class HostedPagesTests
{
    [Fact]
    public async Task ATokenWhoseSignInIsNotRecentNeitherEnrolsNorTurnsTwoFactorOn()
    {
        await using var host = NewTwoFactorHost();
        var editor = await CreateUserAsync(host);
        var tokens = await host.SignInAsync(editor);
        AuthTime(tokens.AccessToken).ShouldBe(host.Time.GetUtcNow().ToUnixTimeSeconds());
        using var client = WithToken(host, tokens.AccessToken);

        var enrolled = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/enrol");
        enrolled.Status.ShouldBe(HttpStatusCode.OK, enrolled.Body?.ToJsonString());
        var key = enrolled.Json["sharedKey"]!.GetValue<string>();

        // The token is still good, but its sign-in is older than the server takes for this: a right code turns nothing on.
        host.Time.Advance(CurrentUser.RecentSignIn);
        var late = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/verify", Code(Totp(host, key)));
        late.Status.ShouldBe(HttpStatusCode.Forbidden, late.Body?.ToJsonString());
        late.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.RecentSignInRequired);
        (await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/enrol")).Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.RecentSignInRequired);
        var status = await AdminFixture.SendAsync(client, HttpMethod.Get, "me/two-factor");
        status.Json["enabled"]!.GetValue<bool>().ShouldBeFalse();
        status.Json["pending"]!.GetValue<bool>().ShouldBeTrue();

        // A token the refresh token gives is of the same sign-in.
        using (var refreshed = await RefreshAsync(host, tokens.RefreshToken!))
        {
            refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync(Token));
            using var renewed = WithToken(host, json.RootElement.GetProperty("access_token").GetString()!);
            (await AdminFixture.SendAsync(renewed, HttpMethod.Get, "me")).Status.ShouldBe(HttpStatusCode.OK);
            (await AdminFixture.SendAsync(renewed, HttpMethod.Post, "me/two-factor/enrol")).Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.RecentSignInRequired);
        }

        // A new sign-in does both.
        using var fresh = await host.AsUserAsync(editor);
        var again = await AdminFixture.SendAsync(fresh, HttpMethod.Post, "me/two-factor/enrol");
        again.Status.ShouldBe(HttpStatusCode.OK, again.Body?.ToJsonString());
        var verified = await AdminFixture.SendAsync(fresh, HttpMethod.Post, "me/two-factor/verify", Code(Totp(host, again.Json["sharedKey"]!.GetValue<string>())));
        verified.Status.ShouldBe(HttpStatusCode.OK, verified.Body?.ToJsonString());
    }

    // Anyone who knows the name locks the account out. The user who is signed in elsewhere still leaves the forced password
    // change and still enrols, on the hosted page and through /me, while new sign-ins stay locked out.
    [Fact]
    public async Task WrongPasswordsFromAnotherAddressDoNotKeepASignedInUserFromTheForcedChangeOrFromEnrolling()
    {
        await using var host = NewTwoFactorHost();
        var editor = await CreateUserAsync(host);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.RequirePasswordChangeAsync((await users.FindByIdAsync(editor.Id.ToString()))!)).Succeeded.ShouldBeTrue();
        }

        using var browser = new HostedPagesBrowser(host, "10.3.1.1");
        await SignInAsync(browser, editor.Email);
        await LockOutAsync(host, editor, "10.3.1.2");

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var sent = await browser.GetAsync(request.Url);
        var change = sent.Headers.Location!.OriginalString;
        change.ShouldStartWith("/account/change-password?ReturnUrl=");
        using var changed = await browser.SubmitAsync(change, new Dictionary<string, string>(NewPasswordFields(NewPassword)) { ["Input.CurrentPassword"] = Password });
        changed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using var done = await browser.FollowAsync(changed);
        var access = (await request.ExchangeAsync(host, done)).GetProperty("access_token").GetString()!;

        // Through /me, with the token of that session.
        using var client = WithToken(host, access);
        var enrolled = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/enrol");
        enrolled.Status.ShouldBe(HttpStatusCode.OK, enrolled.Body?.ToJsonString());
        await LockOutAsync(host, editor, "10.3.1.3");
        var verified = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/verify", Code(Totp(host, enrolled.Json["sharedKey"]!.GetValue<string>())));
        verified.Status.ShouldBe(HttpStatusCode.OK, verified.Body?.ToJsonString());

        // On the hosted page, for another user whose cookie is from before the lockout.
        var other = await CreateUserAsync(host);
        using var second = new HostedPagesBrowser(host, "10.3.1.4");
        await SignInAsync(second, other.Email);
        await LockOutAsync(host, other, "10.3.1.5");
        var key = SharedKeyOf(await second.PageAsync("/account/enable-two-factor")).Replace(" ", "", StringComparison.Ordinal);
        using var turnedOn = await second.SubmitAsync("/account/enable-two-factor", new Dictionary<string, string> { ["Input.Code"] = Totp(host, key) });
        (await turnedOn.Content.ReadAsStringAsync(Token)).ShouldContain("Two-factor sign-in is on.");
        (await LockedOutAsync(host, other)).ShouldBeTrue();
    }

    // What a session gets wrong is counted for the user's sessions, so a session that leaked guesses no more of the current
    // password or of the codes than a sign-in would, and a right one is refused unchecked until the count's time is over.
    [Fact]
    public async Task ASessionsWrongPasswordsAndCodesAreBoundedAndRefusedForTheLockoutsTime()
    {
        await using var host = NewTwoFactorHost();
        var lockout = new AdminUserOptions().Lockout;
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host, "10.3.2.1");
        await SignInAsync(browser, editor.Email);

        for (var attempt = 0; attempt < lockout.MaxFailedAccessAttempts; attempt++)
        {
            using var wrong = await browser.SubmitAsync("/account/change-password", new Dictionary<string, string>(NewPasswordFields(NewPassword)) { ["Input.CurrentPassword"] = "not the password" });
            wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var refused = await browser.SubmitAsync("/account/change-password", new Dictionary<string, string>(NewPasswordFields(NewPassword)) { ["Input.CurrentPassword"] = Password }))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await refused.Content.ReadAsStringAsync(Token)).ShouldContain("The account is locked out for now");
        }

        host.Time.Advance(lockout.DefaultLockoutTimeSpan + TimeSpan.FromSeconds(1));
        using (var changed = await browser.SubmitAsync("/account/change-password", new Dictionary<string, string>(NewPasswordFields(NewPassword)) { ["Input.CurrentPassword"] = Password }))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        // Codes: the user has two-factor, and a token of it tries to turn it off.
        var guarded = await CreateUserAsync(host);
        var (key, _) = await EnableTwoFactorAsync(host, guarded);
        using var client = await host.AsUserAsync(guarded);
        await LockOutAsync(host, guarded, "10.3.2.2");
        for (var attempt = 0; attempt < lockout.MaxFailedAccessAttempts; attempt++)
        {
            (await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/recovery-codes", Code(WrongCode(host, key)))).Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.InvalidTwoFactorCode);
        }

        var right = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/recovery-codes", Code(Totp(host, key)));
        right.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        right.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.InvalidTwoFactorCode);

        host.Time.Advance(lockout.DefaultLockoutTimeSpan + TimeSpan.FromSeconds(1));
        var after = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/recovery-codes", Code(Totp(host, key)));
        after.Status.ShouldBe(HttpStatusCode.OK, after.Body?.ToJsonString());
    }

    // A user of a provider only has no current password to give on /account/change-password. The flag is refused for it,
    // and one that carries the flag from before is not sent there: it gets its code and refreshes its tokens.
    [Fact]
    public async Task AUserWithoutAPasswordIsNeverHeldAtTheForcedPasswordChange()
    {
        using var admin = await _host.AsUserAsync(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var passwordless = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

        var refused = await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{passwordless.Id:D}", """{ "mustChangePassword": true }""");
        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, refused.Body?.ToJsonString());
        refused.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("mustChangePassword");
        refused.Json["errors"]![0]!["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.InvalidUser);
        (await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{passwordless.Id:D}", """{ "mustChangePassword": false }""")).Status.ShouldBe(HttpStatusCode.OK);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<CmsUser>().Where(user => user.Id == passwordless.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.MustChangePassword, true), Token);
        }

        var tokens = await _host.SignInAsync(passwordless);
        using (var refreshed = await RefreshAsync(_host, tokens.RefreshToken!))
        {
            refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // With a password the flag holds as before.
        var editor = await CreateUserAsync(_host);
        (await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{editor.Id:D}", """{ "mustChangePassword": true }""")).Status.ShouldBe(HttpStatusCode.OK);
        (await Should.ThrowAsync<InvalidOperationException>(() => _host.SignInAsync(editor))).Message.ShouldContain("/account/change-password");
    }

    // The page answers a name that is an address other than the invitation's the same, whether or not a user has it, so an
    // invitation link is no way to learn whose addresses the users have.
    [Fact]
    public async Task TheInvitationPageDoesNotTellWhetherAnAddressIsAUsers()
    {
        var known = await CreateUserAsync(_host);
        var invited = $"invitee-{Guid.NewGuid():N}@example.com";
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var role = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Role>().AsNoTracking().SingleAsync(role => role.Name == BuiltInRoles.EditorName && role.SiteId == null, Token);
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), Origin.Seed, new SiteId(DefaultSite.Id));
            await scope.ServiceProvider.GetRequiredService<InvitationService>().InviteAsync(invited, [role.Id], new Uri("https://localhost/account/accept-invitation"), Token);
        }

        var link = _emails.LinkTo(invited).PathAndQuery;
        using var browser = new HostedPagesBrowser(_host, "10.3.4.1");
        async Task<string> AnswerAsync(string userName)
        {
            using var answer = await browser.SubmitAsync(link, new Dictionary<string, string>(NewPasswordFields(Password)) { ["Input.UserName"] = userName });
            answer.StatusCode.ShouldBe(HttpStatusCode.OK);
            return ErrorsOf(await answer.Content.ReadAsStringAsync(Token));
        }

        var ofAUser = await AnswerAsync(known.Email);
        var ofNoOne = await AnswerAsync($"nobody-{Guid.NewGuid():N}@example.com");

        ofAUser.ShouldContain("A user name that is an email address must be the address this invitation was sent to.");
        ofNoOne.ShouldBe(ofAUser);
        ofAUser.ShouldNotContain("taken");

        using var accepted = await browser.SubmitAsync(link, new Dictionary<string, string>(NewPasswordFields(Password)) { ["Input.UserName"] = invited });
        accepted.Headers.Location!.OriginalString.ShouldBe("/account/login?Status=invitation-accepted");
    }

    // Wrong passwords on the login page from an address of its own, as someone who only knows the name sends them.
    private static async Task LockOutAsync(FluentCmsTestHost host, TestUser user, string address)
    {
        using var attacker = new HostedPagesBrowser(host, address);
        for (var attempt = 0; attempt < new AdminUserOptions().Lockout.MaxFailedAccessAttempts; attempt++)
        {
            using var wrong = await attacker.SubmitAsync("/account/login", Credentials(user.Email, "not the password"));
            wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await LockedOutAsync(host, user)).ShouldBeTrue();
    }

    private static HttpClient WithToken(FluentCmsTestHost host, string accessToken)
    {
        var client = host.CreateSecureClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static long AuthTime(string jwt)
    {
        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Split('.')[1]));
        return payload.RootElement.GetProperty("auth_time").GetInt64();
    }

    // The validation summary of a page, which holds what the server answered and none of what the form sent back.
    private static string ErrorsOf(string page)
    {
        var start = page.IndexOf("validation-summary", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, page);
        return page[start..page.IndexOf("</div>", start, StringComparison.Ordinal)];
    }
}
