using System.Net;
using System.Text.Json;
using System.Web;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Authentication;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The admin's own sign-in against the real authorization server of the test host: the authorize redirect with PKCE, which the
// browser follows with the user's cookie of the hosted pages (OAuthFlows), the code exchange on the callback, the refresh
// with rotation, and the sign-out.
public sealed class SignInTests : IAsyncLifetime
{
    private const string Password = "Correct-Horse-Battery-9";
    private const string NewPassword = "A-New-Longer-Passphrase-7";

    private readonly AdminHost _admin = new();

    private FluentCmsTestHost Host => _admin.Host;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _admin.DisposeAsync();

    [Fact]
    public async Task TheAdminSignsInWithItsCodeFlowAndCallsTheManagementApi()
    {
        var editor = await Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: AdminHost.Token);
        using var tab = NewTab();
        using var browser = await BrowserAsync(editor);

        var completion = await tab.SignInAsync(browser);

        completion.ShouldBe(SignInCompletion.Succeeded("content/article"));
        tab.Session.User.ShouldNotBeNull().Subject.ShouldBe(editor.Id.ToString("D"));
        tab.Session.User.Name.ShouldBe(editor.Email);
        tab.RefreshToken.ShouldNotBeNullOrEmpty();
        (await tab.Client.GetMeAsync(AdminHost.Token)).Name.ShouldBe(editor.Email);
    }

    [Fact]
    public async Task ACodeIsRedeemedOnce()
    {
        var editor = await Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: AdminHost.Token);
        using var tab = NewTab();
        using var browser = await BrowserAsync(editor);
        await tab.Session.SignInAsync("", AdminHost.Token);
        var callback = await browser.FollowToAsync(tab.Navigation.Last!.PathAndQuery, AdminTab.AdminBase);
        var query = HttpUtility.ParseQueryString(callback.Query);
        (await tab.Session.CompleteSignInAsync(query["code"], query["state"], null, AdminHost.Token)).Failure.ShouldBeNull();

        // The state was used, and the server refuses the code to anyone else.
        (await tab.Session.CompleteSignInAsync(query["code"], query["state"], null, AdminHost.Token)).Failure.ShouldBe(SignInFailure.UnknownState);
        using var client = Host.CreateSecureClient();
        using var replay = await PostTokenAsync(client, new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = query["code"]!,
            ["redirect_uri"] = AdminTab.AdminBase + AdminSession.CallbackRoute,
            ["client_id"] = AdminTab.AdminClientId,
            ["code_verifier"] = ProofKey.NewSecret(),
        });
        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ARefreshRotatesTheTokenAndAUsedOneIsAcceptedWithinTheLeewayOnly()
    {
        var editor = await Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: AdminHost.Token);
        using var tab = NewTab();
        using var browser = await BrowserAsync(editor);
        await tab.SignInAsync(browser);
        var first = tab.RefreshToken!;
        var firstAccess = (await tab.Session.GetAccessTokenAsync(AdminHost.Token))!;

        (await tab.Session.RefreshAsync(firstAccess, AdminHost.Token)).ShouldBeTrue();

        var second = tab.RefreshToken!;
        second.ShouldNotBe(first);
        (await tab.Session.GetAccessTokenAsync(AdminHost.Token)).ShouldNotBe(firstAccess);
        (await tab.Client.GetMeAsync(AdminHost.Token)).Name.ShouldBe(editor.Email);

        // A retry of the same refresh, as a tab that lost the answer makes, within OpenIddict's 30 seconds of leeway.
        using var client = Host.CreateSecureClient();
        Host.Time.Advance(TimeSpan.FromSeconds(10));
        using (var within = await PostTokenAsync(client, Refresh(first)))
        {
            within.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        Host.Time.Advance(TimeSpan.FromMinutes(1));
        using var outside = await PostTokenAsync(client, Refresh(first));
        outside.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorAsync(outside)).ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task ARefreshTheServerRefusesEndsTheSessionAndGoesToSignIn()
    {
        var editor = await Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: AdminHost.Token);
        using var tab = NewTab();
        using var browser = await BrowserAsync(editor);
        await tab.SignInAsync(browser);
        var access = (await tab.Session.GetAccessTokenAsync(AdminHost.Token))!;

        // Another holder of the token used it, long enough ago that the tab's own use is refused as a replay.
        using var client = Host.CreateSecureClient();
        using (var used = await PostTokenAsync(client, Refresh(tab.RefreshToken!)))
        {
            used.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        Host.Time.Advance(TimeSpan.FromMinutes(1));
        (await tab.Session.RefreshAsync(access, AdminHost.Token)).ShouldBeFalse();

        tab.Session.User.ShouldBeNull();
        tab.Storage.Get(AdminSession.SessionKey).ShouldBeNull();
        tab.Navigation.Last!.GetLeftPart(UriPartial.Path).ShouldBe("https://localhost/connect/authorize");
    }

    [Fact]
    public async Task SignOutRevokesTheRefreshTokenAndTheLogoutReturnsToTheAdmin()
    {
        var editor = await Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: AdminHost.Token);
        using var tab = NewTab();
        using var browser = await BrowserAsync(editor);
        await tab.SignInAsync(browser);
        var refreshToken = tab.RefreshToken!;

        await tab.Session.SignOutAsync(AdminHost.Token);

        tab.Storage.Keys.ShouldBeEmpty();
        using var client = Host.CreateSecureClient();
        using (var revoked = await PostTokenAsync(client, Refresh(refreshToken)))
        {
            revoked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // Without an id_token_hint the hosted logout page asks, then ends the session and returns to the admin's base.
        var logout = tab.Navigation.Last!;
        logout.GetLeftPart(UriPartial.Path).ShouldBe("https://localhost/connect/logout");
        using var asked = await browser.GetAsync(logout.PathAndQuery);
        asked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var page = await browser.PageAsync(asked.Headers.Location!.OriginalString);
        using var confirmed = await browser.PostAsync("/connect/logout", page, new Dictionary<string, string>());
        confirmed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        confirmed.Headers.Location!.AbsoluteUri.ShouldBe(AdminTab.AdminBase);

        // The server's session is over, so the next sign-in goes to the login page.
        using var next = NewTab();
        await next.Session.SignInAsync("", AdminHost.Token);
        using var authorize = await browser.GetAsync(next.Navigation.Last!.PathAndQuery);
        new Uri(new Uri("https://localhost/"), authorize.Headers.Location!).AbsolutePath.ShouldBe("/account/login");
    }

    [Fact]
    public async Task AForcedPasswordChangeOnTheHostedPageCompletesTheSignInAfterwards()
    {
        var editor = await Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: AdminHost.Token);
        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            var user = (await users.FindByIdAsync(editor.Id.ToString()))!;
            (await users.AddPasswordAsync(user, Password)).Succeeded.ShouldBeTrue();
            (await users.RequirePasswordChangeAsync(user)).Succeeded.ShouldBeTrue();
        }

        using var tab = NewTab();
        using var browser = await BrowserAsync(editor);
        var pages = new List<string>();

        var completion = await tab.SignInAsync(browser, "content/article", async response =>
        {
            var url = response.RequestMessage!.RequestUri!.PathAndQuery;
            var page = await response.Content.ReadAsStringAsync(AdminHost.Token);
            response.Dispose();
            pages.Add(url);
            return await browser.PostAsync(url, page, new Dictionary<string, string>
            {
                ["Input.CurrentPassword"] = Password,
                ["Input.Password"] = NewPassword,
                ["Input.ConfirmPassword"] = NewPassword,
            });
        });

        pages.ShouldHaveSingleItem().ShouldStartWith("/account/change-password?ReturnUrl=");
        completion.ShouldBe(SignInCompletion.Succeeded("content/article"));
        (await tab.Client.GetMeAsync(AdminHost.Token)).Name.ShouldBe(editor.Email);
    }

    private static Dictionary<string, string> Refresh(string refreshToken) => new()
    {
        ["grant_type"] = "refresh_token",
        ["refresh_token"] = refreshToken,
        ["client_id"] = AdminTab.AdminClientId,
    };

    private static async Task<HttpResponseMessage> PostTokenAsync(HttpClient client, Dictionary<string, string> form)
    {
        using var content = new FormUrlEncodedContent(form);
        return await client.PostAsync("/connect/token", content, AdminHost.Token);
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AdminHost.Token));
        return json.RootElement.GetProperty("error").GetString();
    }

    private AdminTab NewTab() => new(Host, _admin.ServerHandler);

    private async Task<HostedBrowser> BrowserAsync(TestUser user) => new(_admin.ServerHandler, await Host.SignInCookieAsync(user));
}
