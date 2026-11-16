using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Telemetry;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// How long sign-ins and sessions last, and what ends them (#2854).
public sealed partial class AuthorizationServerTests
{
    [Fact]
    public async Task ALogoutPostWithoutTheLogoutPagesTokenSignsNoOneOut()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var cookie = await _host.SignInCookieAsync(editor);
        using var client = _host.CreateSecureClient();
        var tokens = await SignInAsync(client, cookie, "openid management");

        // A form on another site that posts here: the logout page asks, and the cookie stays.
        using (var forged = await SendAsync(client, HttpMethod.Post, "/connect/logout", cookie, Form(("state", "xyz"))))
        {
            forged.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            forged.Headers.Location!.OriginalString.ShouldBe("/account/logout?state=xyz");
            forged.Headers.Contains("Set-Cookie").ShouldBeFalse();
        }

        // A cross-site POST does not carry the SameSite cookie, so even a valid hint only leads to the page.
        using (var hinted = await SendAsync(client, HttpMethod.Post, "/connect/logout", "", Form(("id_token_hint", tokens.IdentityToken!))))
        {
            hinted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            new Uri(client.BaseAddress!, hinted.Headers.Location!).AbsolutePath.ShouldBe("/account/logout");
            hinted.Headers.Contains("Set-Cookie").ShouldBeFalse();
        }

        // The session's own hint with its cookie ends it.
        using var own = await SendAsync(client, HttpMethod.Post, "/connect/logout", cookie, Form(("id_token_hint", tokens.IdentityToken!)));
        own.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        own.Headers.GetValues("Set-Cookie").ShouldContain(value => value.StartsWith(".AspNetCore.Identity.Application=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PromptLoginAndAnExpiredMaxAgeAskForANewSignInAndKeepTheCookie()
    {
        // The separator of the mark's fields in a client id keeps nothing from working.
        const string OtherClientId = "another|first-party-client";
        await using var host = NewHost().Configure(cms => cms.Clients.Applications[OtherClientId] = new ClientRegistration
        {
            RedirectUris = { FluentCmsTestHost.OAuthRedirectUri },
            Scopes = { "openid" },
        });
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var cookie = await host.SignInCookieAsync(editor);
        using var client = host.CreateSecureClient();
        var otherPath = AuthorizePath().Replace("client_id=" + FluentCmsTestHost.OAuthClientId, "client_id=" + Uri.EscapeDataString(OtherClientId), StringComparison.Ordinal);

        // Without a cookie, the sign-in the user makes answers prompt=login and max_age, so the request comes back without
        // them.
        using (var signedOut = await SendAsync(client, HttpMethod.Get, AuthorizePath() + "&prompt=login%20consent&max_age=60", ""))
        {
            var back = SignInAgain(client, signedOut);
            back.ShouldContain("prompt=consent");
            back.ShouldNotContain("login");
            back.ShouldNotContain("max_age");
        }

        // The cookie's own sign-in answers no mark, even in the second it was made.
        using (var now = await SendAsync(client, HttpMethod.Get, AuthorizePath() + "&prompt=login", cookie))
        using (var again = await SendAsync(client, HttpMethod.Get, SignInAgain(client, now), cookie))
        {
            SignInAgain(client, again);
        }

        host.Time.Advance(TimeSpan.FromMinutes(5));
        var between = await host.SignInCookieAsync(editor);
        host.Time.Advance(TimeSpan.FromMinutes(5));

        // prompt=login: the request comes back as it was, with the mark of when it asked, and without a sign-in since the
        // request it asks again: neither the cookie it found nor a newer one from before the request answers it.
        string returnUrl;
        using (var asked = await SendAsync(client, HttpMethod.Get, AuthorizePath() + "&prompt=login%20consent", cookie))
        {
            returnUrl = SignInAgain(client, asked);
        }

        returnUrl.ShouldContain("prompt=login%20consent");
        returnUrl.ShouldContain(Reauthentication.Parameter + "=");
        foreach (var older in new[] { cookie, between })
        {
            using var back = await SendAsync(client, HttpMethod.Get, returnUrl, older);
            SignInAgain(client, back);
        }

        string otherReturnUrl;
        using (var asked = await SendAsync(client, HttpMethod.Get, otherPath + "&prompt=login", cookie))
        {
            otherReturnUrl = SignInAgain(client, asked);
        }

        // A sign-in since answers the mark of that request, and neither a changed mark nor the mark on a request of another
        // client or with another PKCE challenge, which are all the mark is bound to.
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var newer = await host.SignInCookieAsync(editor);
        var mark = "&prompt=login&" + Reauthentication.Parameter + "=";
        var value = QueryHelpers.ParseQuery(new Uri(client.BaseAddress!, returnUrl).Query)[Reauthentication.Parameter].ToString();
        foreach (var path in new[]
        {
            AuthorizePath() + mark + Uri.EscapeDataString(value[..^4] + "AAAA"),
            AuthorizePath().Replace("abcdefghij", "jihgfedcba", StringComparison.Ordinal) + mark + Uri.EscapeDataString(value),
            otherPath + mark + Uri.EscapeDataString(value),
        })
        {
            using var refused = await SendAsync(client, HttpMethod.Get, path, newer);
            SignInAgain(client, refused);
        }

        foreach (var own in new[] { returnUrl, otherReturnUrl })
        {
            using var answered = await SendAsync(client, HttpMethod.Get, own, newer);
            answered.Headers.Location!.AbsoluteUri.ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=", Case.Sensitive, own);
        }

        // The mark lasts 15 minutes from the request.
        host.Time.Advance(TimeSpan.FromMinutes(15));
        using (var expired = await SendAsync(client, HttpMethod.Get, returnUrl, newer))
        {
            SignInAgain(client, expired);
        }

        // A max_age the sign-in is older than, in the same way.
        using (var old = await SendAsync(client, HttpMethod.Get, AuthorizePath() + "&max_age=60", newer))
        {
            returnUrl = SignInAgain(client, old);
        }

        returnUrl.ShouldContain("max_age=60");
        using (var back = await SendAsync(client, HttpMethod.Get, returnUrl, newer))
        {
            SignInAgain(client, back);
        }

        using (var silent = await SendAsync(client, HttpMethod.Get, AuthorizePath() + "&max_age=60&prompt=none", cookie))
        {
            silent.Headers.Location!.AbsoluteUri.ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?error=login_required");
        }

        using var fresh = await SendAsync(client, HttpMethod.Get, AuthorizePath() + "&max_age=3600", cookie);
        fresh.Headers.Location!.AbsoluteUri.ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");
    }

    [Fact]
    public async Task TheIdentityTokenSaysWhenTheUserSignedIn()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var signedIn = _host.Time.GetUtcNow().ToUnixTimeSeconds();
        _host.Time.Advance(TimeSpan.FromMinutes(1));

        var tokens = await _host.SignInAsync(editor, "openid", "offline_access", "management");

        Payload(tokens.IdentityToken!).GetProperty(OpenIddictConstants.Claims.AuthenticationTime).GetInt64().ShouldBeInRange(signedIn, signedIn + 61);
        Payload(tokens.AccessToken).TryGetProperty(OpenIddictConstants.Claims.AuthenticationTime, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task TokenAndCookieLifetimesComeFromTheClientOptions()
    {
        var defaults = await LifetimesAsync(_host);
        defaults.AccessToken.ShouldBe(TimeSpan.FromHours(1));
        defaults.RefreshToken.ShouldBe(TimeSpan.FromDays(14));
        defaults.Cookie.ShouldBe(TimeSpan.FromDays(14));

        await using var host = NewHost().Configure(cms =>
        {
            cms.Clients.AccessTokenLifetime = TimeSpan.FromMinutes(5);
            cms.Clients.RefreshTokenLifetime = TimeSpan.FromHours(8);
            cms.Clients.SignInCookieLifetime = TimeSpan.FromHours(2);
        });
        var configured = await LifetimesAsync(host);
        configured.AccessToken.ShouldBe(TimeSpan.FromMinutes(5));
        configured.RefreshToken.ShouldBe(TimeSpan.FromHours(8));
        configured.Cookie.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task TheAbsoluteSessionLifetimeEndsASessionHoweverOftenItIsRefreshed()
    {
        await using var host = NewHost().Configure(cms => cms.Clients.AbsoluteSessionLifetime = TimeSpan.FromHours(8));
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var tokens = await host.SignInAsync(editor);
        var cookie = await host.SignInCookieAsync(editor);
        using var client = host.CreateSecureClient();

        for (var hour = 0; hour < 7; hour++)
        {
            host.Time.Advance(TimeSpan.FromHours(1));
            tokens = await TokenAsync(client, Refresh(tokens.RefreshToken!), Token);
        }

        host.Time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        using (var ended = await PostTokenAsync(client, Refresh(tokens.RefreshToken!)))
        {
            ended.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ErrorAsync(ended)).ShouldBe("invalid_grant");
        }

        // The cookie of that sign-in, which slides while it is used, is as old: the user signs in again over it.
        using var authorize = await SendAsync(client, HttpMethod.Get, AuthorizePath(), cookie);
        SignInAgain(client, authorize);
    }

    [Fact]
    public async Task ACodeFlowLateInASignInEndsWithTheSignInsAbsoluteLifetime()
    {
        await using var host = NewHost().Configure(cms => cms.Clients.AbsoluteSessionLifetime = TimeSpan.FromHours(8));
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var cookie = await host.SignInCookieAsync(editor);
        using var client = host.CreateSecureClient();

        host.Time.Advance(TimeSpan.FromHours(7));
        var tokens = await SignInAsync(client, cookie);
        tokens = await TokenAsync(client, Refresh(tokens.RefreshToken!), Token);

        // An hour past the sign-in's lifetime, not the code flow's.
        host.Time.Advance(TimeSpan.FromHours(2));
        using var ended = await PostTokenAsync(client, Refresh(tokens.RefreshToken!));
        ended.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorAsync(ended)).ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task AWrongExpiredOrRevokedKeyGetsOneCodeAndTheServerLogsAndCountsWhy()
    {
        var logs = new LogCapture();
        var refusals = new ConcurrentBag<string?>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == FluentCmsTelemetry.Name && instrument.Name == FluentCmsTelemetry.Instruments.ApiKeysRefused)
                {
                    meter.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == FluentCmsTelemetry.Tags.Code)
                {
                    refusals.Add(tag.Value as string);
                }
            }
        });
        listener.Start();

        await using var host = NewHost().WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)));
        var revoked = await host.CreateApiKeyAsync(FluentCMS.Testing.ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        var expired = await host.CreateApiKeyAsync(FluentCMS.Testing.ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
            (await keys.RevokeAsync(revoked.Id, Token)).ShouldBeTrue();
            await keys.ExpireAsync(expired.Id, host.Time.GetUtcNow().AddMinutes(1), Token);
        }

        host.Time.Advance(TimeSpan.FromMinutes(2));

        var wrong = revoked.Secret[..16] + new string('A', revoked.Secret.Length - 16);
        foreach (var secret in new[] { wrong, revoked.Secret, expired.Secret })
        {
            using var client = host.CreateSecureClient();
            client.DefaultRequestHeaders.Add(ApiKeyAuthentication.Header, secret);
            using var response = await client.GetAsync("/api/management/v1/test/whoami", Token);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await JsonAsync(response)).GetProperty("code").GetString().ShouldBe(ApiKeyErrorCodes.InvalidKey);
        }

        var logged = logs.Entries.Where(entry => entry.EventId.Id == ApiKeyLog.RefusedId).ToList();
        logged.Select(entry => (string?)entry.State["Code"]).ShouldBe([ApiKeyErrorCodes.InvalidKey, ApiKeyErrorCodes.Revoked, ApiKeyErrorCodes.Expired]);
        logged.Select(entry => (Guid?)entry.State["KeyId"]).ShouldBe([null, revoked.Id, expired.Id]);
        var secrets = new[] { revoked.Secret[8..], expired.Secret[8..] };
        logged.ShouldAllBe(entry => !secrets.Any(secret => entry.Message.Contains(secret, StringComparison.Ordinal)));
        refusals.ShouldContain(ApiKeyErrorCodes.Revoked);
        refusals.ShouldContain(ApiKeyErrorCodes.Expired);
        refusals.ShouldContain(ApiKeyErrorCodes.InvalidKey);
    }

    // The login page the authorize endpoint sends the user to, with the cookie left, and the request to come back with.
    private static string SignInAgain(HttpClient client, HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = new Uri(client.BaseAddress!, response.Headers.Location!);
        location.AbsolutePath.ShouldBe("/account/login");
        (response.Headers.TryGetValues("Set-Cookie", out var set) ? set : []).ShouldNotContain(value => value.StartsWith(".AspNetCore.Identity.Application=;", StringComparison.Ordinal));
        return QueryHelpers.ParseQuery(location.Query)["ReturnUrl"].ToString();
    }

    private static async Task<(TimeSpan AccessToken, TimeSpan RefreshToken, TimeSpan Cookie)> LifetimesAsync(FluentCmsTestHost host)
    {
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var tokens = await host.SignInAsync(editor);
        var payload = Payload(tokens.AccessToken);

        await using var scope = host.Services.CreateAsyncScope();
        var refresh = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OpenIddictEntityFrameworkCoreToken<Guid>>().AsNoTracking()
            .SingleAsync(row => row.Subject == editor.Id.ToString("D") && row.Type!.EndsWith(OpenIddictConstants.TokenTypeHints.RefreshToken), Token);
        var cookie = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        return (
            TimeSpan.FromSeconds(payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64()),
            refresh.ExpirationDate!.Value - refresh.CreationDate!.Value,
            cookie.ExpireTimeSpan);
    }

    private static FormUrlEncodedContent Form(params (string Name, string Value)[] fields) =>
        new(fields.Select(field => KeyValuePair.Create(field.Name, field.Value)));
}
