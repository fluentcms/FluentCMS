using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// A new security stamp of an admin user revokes every session of the user, so an access token issued before it is refused
// at the next request, as its refresh token and codes are (#3202).
public sealed partial class HostedPagesTests
{
    [Fact]
    public async Task APasswordChangeOnThePageRevokesTheUsersTokensAndKeepsTheBrowserThatMadeItSignedIn()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.3.1");
        await SignInAsync(browser, editor.Email);
        var own = await CodeFlowAsync(_host, browser);
        var otherDevice = await _host.SignInAsync(editor);
        (await MeAsync(_host, Text(own, "access_token"))).ShouldBe(HttpStatusCode.OK);
        (await MeAsync(_host, otherDevice.AccessToken)).ShouldBe(HttpStatusCode.OK);

        using (var changed = await browser.SubmitAsync("/account/change-password", new Dictionary<string, string>(NewPasswordFields(NewPassword)) { ["Input.CurrentPassword"] = Password }))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        (await MeAsync(_host, Text(own, "access_token"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(_host, otherDevice.AccessToken)).ShouldBe(HttpStatusCode.Unauthorized);
        (await RefusedAsync(_host, Text(own, "refresh_token"))).ShouldBe("invalid_grant");
        (await RefusedAsync(_host, otherDevice.RefreshToken!)).ShouldBe("invalid_grant");

        // The browser keeps its cookie, which carries the new stamp, and its next code needs no sign-in.
        using (var page = await browser.GetAsync("/account/change-password"))
        {
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await MeAsync(_host, Text(await CodeFlowAsync(_host, browser), "access_token"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task APasswordResetRevokesTheUsersAccessTokens()
    {
        var editor = await CreateUserAsync(_host);
        var tokens = await _host.SignInAsync(editor);
        (await MeAsync(_host, tokens.AccessToken)).ShouldBe(HttpStatusCode.OK);

        using var browser = new HostedPagesBrowser(_host, "10.1.3.2");
        using (var sent = await browser.SubmitAsync("/account/forgot-password", new Dictionary<string, string> { ["Input.Email"] = editor.Email }))
        {
            sent.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await AccountMailJobs.RunAsync(_host);
        using (var reset = await browser.SubmitAsync(_emails.LinkTo(editor.Email).PathAndQuery, NewPasswordFields(NewPassword)))
        {
            reset.Headers.Location!.OriginalString.ShouldBe("/account/login?Status=password-reset");
        }

        (await MeAsync(_host, tokens.AccessToken)).ShouldBe(HttpStatusCode.Unauthorized);
        (await RefusedAsync(_host, tokens.RefreshToken!)).ShouldBe("invalid_grant");
    }

    // A new name or address and a deactivation give a new stamp; the flag of a forced password change does not, and ends nothing.
    [Theory]
    [InlineData("name", true)]
    [InlineData("email", true)]
    [InlineData("deactivate", true)]
    [InlineData("mustChangePassword", false)]
    public async Task AnAdministratorsChangeThatGivesANewStampRevokesTheUsersAccessTokens(string change, bool revokes)
    {
        using var admin = await _host.AsUserAsync(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var editor = await CreateUserAsync(_host);
        var tokens = await _host.SignInAsync(editor);
        (await MeAsync(_host, tokens.AccessToken)).ShouldBe(HttpStatusCode.OK);

        var renamed = $"renamed-{Guid.NewGuid():N}@example.com";
        var done = change switch
        {
            "name" => await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{editor.Id:D}", $$"""{ "userName": "{{renamed}}" }"""),
            "email" => await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{editor.Id:D}", $$"""{ "email": "{{renamed}}" }"""),
            "deactivate" => await AdminFixture.SendAsync(admin, HttpMethod.Post, $"users/{editor.Id:D}/deactivate"),
            _ => await AdminFixture.SendAsync(admin, HttpMethod.Patch, $"users/{editor.Id:D}", """{ "mustChangePassword": true }"""),
        };
        done.Status.ShouldBe(HttpStatusCode.OK, done.Body?.ToJsonString());

        (await MeAsync(_host, tokens.AccessToken)).ShouldBe(revokes ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemovingAnExternalLoginRevokesTheUsersAccessTokens()
    {
        var host = _sso;
        using var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await AddLoginAsync(host, editor.Id, Corp, $"sub-{editor.Id:N}");
        await AddLoginAsync(host, editor.Id, "gone", $"sub-{editor.Id:N}");
        var tokens = await host.SignInAsync(editor);
        (await MeAsync(host, tokens.AccessToken)).ShouldBe(HttpStatusCode.OK);

        (await AdminFixture.SendAsync(admin, HttpMethod.Delete, $"users/{editor.Id:D}/logins/gone")).Status.ShouldBe(HttpStatusCode.NoContent);

        (await MeAsync(host, tokens.AccessToken)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TurningTwoFactorOnOrOffThroughMeRevokesTheUsersAccessTokens()
    {
        var editor = await CreateUserAsync(_host);
        var other = await _host.SignInAsync(editor);
        using var client = await _host.AsUserAsync(editor);
        var key = (await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/enrol")).Json["sharedKey"]!.GetValue<string>();

        // Enrolling keeps the stamp, so the sessions go on until a code turns two-factor on.
        (await MeAsync(_host, other.AccessToken)).ShouldBe(HttpStatusCode.OK);
        var verified = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/verify", Code(Totp(key)));
        verified.Status.ShouldBe(HttpStatusCode.OK, verified.Body?.ToJsonString());

        (await AdminFixture.SendAsync(client, HttpMethod.Get, "me")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(_host, other.AccessToken)).ShouldBe(HttpStatusCode.Unauthorized);

        using var again = await _host.AsUserAsync(editor);
        var later = await _host.SignInAsync(editor);
        var off = await AdminFixture.SendAsync(again, HttpMethod.Post, "me/two-factor/disable", Code(verified.Json["recoveryCodes"]![0]!.GetValue<string>()));
        off.Status.ShouldBe(HttpStatusCode.NoContent, off.Body?.ToJsonString());

        (await AdminFixture.SendAsync(again, HttpMethod.Get, "me")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(_host, later.AccessToken)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TurningTwoFactorOnOnTheHostedPageRevokesTheUsersAccessTokensAndKeepsTheBrowserSignedIn()
    {
        var editor = await CreateUserAsync(_host);
        var tokens = await _host.SignInAsync(editor);
        using var browser = new HostedPagesBrowser(_host, "10.1.3.3");
        await SignInAsync(browser, editor.Email);

        var key = SharedKeyOf(await browser.PageAsync("/account/enable-two-factor")).Replace(" ", "", StringComparison.Ordinal);
        using (var enabled = await browser.SubmitAsync("/account/enable-two-factor", new Dictionary<string, string> { ["Input.Code"] = Totp(key) }))
        {
            enabled.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await MeAsync(_host, tokens.AccessToken)).ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(_host, Text(await CodeFlowAsync(_host, browser), "access_token"))).ShouldBe(HttpStatusCode.OK);
    }
}
