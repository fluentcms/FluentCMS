using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
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

    [Fact]
    public async Task AnAuthenticatorCodeIsTakenOnceAtEnrolmentAndAtSignIn()
    {
        var editor = await CreateUserAsync(_host);
        var (key, _) = await EnableTwoFactorAsync(_host, editor);
        var enrolled = TotpAt(key, _host.Time.GetUtcNow().ToUnixTimeSeconds() / 30);

        // The code that turned two-factor on does not sign in.
        using (var browser = new HostedPagesBrowser(_host, "10.1.5.2"))
        {
            using var password = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password));
            var twoFactor = password.Headers.Location!.OriginalString;
            using (var replayed = await browser.SubmitAsync(twoFactor, new Dictionary<string, string> { ["Input.Code"] = enrolled }))
            {
                replayed.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            using var fresh = await browser.SubmitAsync(twoFactor, new Dictionary<string, string> { ["Input.Code"] = Totp(_host, key) });
            fresh.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        // Nor does the code of a sign-in sign in a second time, nor one of an earlier step that is still in the window.
        var used = TotpAt(key, _host.Time.GetUtcNow().ToUnixTimeSeconds() / 30);
        using (var browser = new HostedPagesBrowser(_host, "10.1.5.3"))
        {
            using var password = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password));
            var twoFactor = password.Headers.Location!.OriginalString;
            using (var again = await browser.SubmitAsync(twoFactor, new Dictionary<string, string> { ["Input.Code"] = used }))
            {
                again.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            using (var earlier = await browser.SubmitAsync(twoFactor, new Dictionary<string, string> { ["Input.Code"] = enrolled }))
            {
                earlier.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            browser.HasCookie(SignInCookie).ShouldBeFalse();
        }
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

    private static async Task<bool> LockedOutAsync(FluentCmsTestHost host, TestUser user)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        return await users.IsLockedOutAsync((await users.FindByIdAsync(user.Id.ToString()))!);
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
