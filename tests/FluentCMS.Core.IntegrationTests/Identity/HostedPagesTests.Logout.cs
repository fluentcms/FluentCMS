using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentCMS.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// What a logout ends of the sessions of its user (#3198).
public sealed partial class HostedPagesTests
{
    [Fact]
    public async Task AConfirmedLogoutRevokesTheTokensOfItsSessionAndLeavesTheUsersOtherSessions()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);
        using var otherDevice = new HostedPagesBrowser(_host, "10.1.0.9");
        await SignInAsync(browser, editor.Email);
        await SignInAsync(otherDevice, editor.Email);
        var tokens = await CodeFlowAsync(browser);
        var other = await CodeFlowAsync(otherDevice);

        // A logout that is not confirmed, by a GET without a hint or a POST without the page's token, revokes nothing.
        using (var asked = await browser.GetAsync("/connect/logout"))
        {
            asked.Headers.Location!.OriginalString.ShouldBe("/account/logout");
        }

        using (var forged = await browser.PostAsync("/connect/logout", new Dictionary<string, string>()))
        {
            forged.Headers.Location!.OriginalString.ShouldBe("/account/logout");
        }

        var refreshToken = await RefreshAsync(Text(tokens, "refresh_token"));
        (await MeAsync(Text(tokens, "access_token"))).ShouldBe(HttpStatusCode.OK);

        await SignOutAsync(browser);

        (await RefusedAsync(refreshToken)).ShouldBe("invalid_grant");
        (await MeAsync(Text(tokens, "access_token"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await RefreshAsync(Text(other, "refresh_token"))).ShouldNotBeNullOrEmpty();
        (await MeAsync(Text(other, "access_token"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ALogoutWithAHintRevokesOnlyWhenItEndsTheSessionOfItsCookie()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);
        await SignInAsync(browser, editor.Email);
        var tokens = await CodeFlowAsync(browser);
        var hint = "/connect/logout?id_token_hint=" + Uri.EscapeDataString(Text(tokens, "id_token"));

        // Without the cookie there is no session to end, and the hint alone revokes nothing.
        using (var stranger = new HostedPagesBrowser(_host, "10.1.0.10"))
        using (var returned = await stranger.GetAsync(hint))
        {
            returned.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var refreshToken = await RefreshAsync(Text(tokens, "refresh_token"));

        using (var signedOut = await browser.GetAsync(hint))
        {
            signedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();
        (await RefusedAsync(refreshToken)).ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task ALogoutLeavesTheConsentOfAThirdPartyClient()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);
        await SignInAsync(browser, editor.Email);

        var request = AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid management");
        using var asked = await browser.GetAsync(request.Url);
        var consent = asked.Headers.Location!.OriginalString;
        var page = await browser.PageAsync(consent);
        using var allowed = await browser.PostAsync(consent, page, new Dictionary<string, string> { ["decision"] = "allow" });
        using var done = await browser.FollowAsync(allowed, ThirdPartyRedirectUri);
        var accessToken = Text(await request.ExchangeAsync(_host, done), "access_token");

        await SignOutAsync(browser);

        // The consent is no session's: the client keeps its token, and the next sign-in is not asked again.
        (await MeAsync(accessToken)).ShouldBe(HttpStatusCode.OK);
        await SignInAsync(browser, editor.Email);
        using var again = await browser.GetAsync(AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid management").Url);
        again.Headers.Location!.AbsoluteUri.ShouldStartWith(ThirdPartyRedirectUri + "?code=");
    }

    // The code flow of the test client in the browser's session.
    private async Task<JsonElement> CodeFlowAsync(HostedPagesBrowser browser)
    {
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var redirect = await browser.GetAsync(request.Url);
        return await request.ExchangeAsync(_host, redirect);
    }

    // The rotated refresh token.
    private async Task<string> RefreshAsync(string refreshToken)
    {
        using var response = await PostRefreshAsync(refreshToken);
        var body = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("refresh_token").GetString()!;
    }

    private async Task<string?> RefusedAsync(string refreshToken)
    {
        using var response = await PostRefreshAsync(refreshToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return json.RootElement.GetProperty("error").GetString();
    }

    private async Task<HttpResponseMessage> PostRefreshAsync(string refreshToken)
    {
        using var client = _host.CreateSecureClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = FluentCmsTestHost.OAuthClientId,
        });
        return await client.PostAsync("/connect/token", content, Token);
    }

    private async Task<HttpStatusCode> MeAsync(string accessToken)
    {
        using var client = _host.CreateSecureClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.GetAsync("/api/management/v1/me", Token);
        return response.StatusCode;
    }

    private static string Text(JsonElement tokens, string name) => tokens.GetProperty(name).GetString()!;
}
