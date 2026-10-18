using System.Net;
using System.Text.RegularExpressions;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Identity;

public sealed partial class ExternalSignInTests
{
    private const string Start = "/sign-in/external/start";
    private static readonly NewUser Editor = new("editor@example.com", "Editor", "An0ther!Passw0rd");

    [Fact]
    public async Task Without_a_provider_the_sign_in_page_offers_none_and_the_policy_allows_only_this_origin()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        var signIn = await host.Client.GetAsync("/sign-in");
        var started = await host.SubmitAsync(Start, null, StartFields("oidc"), from: "/sign-in");

        Assert.DoesNotContain("sign-in/external/start", await signIn.Content.ReadAsStringAsync());
        Assert.Contains("form-action 'self';", Policy(signIn));
        Assert.Equal(HttpStatusCode.NotFound, started.StatusCode);
    }

    [Fact]
    public async Task A_user_with_a_matching_verified_email_signs_in_through_the_provider_and_returns_to_the_route()
    {
        await using var provider = await TestOpenIdProvider.StartAsync();
        using var install = new TemporaryInstall();
        await using var host = await StartAsync(install, provider);
        var ownerId = await host.CreateFirstAdministratorAsync();

        var signIn = await host.Client.GetAsync("/sign-in");
        var html = await signIn.Content.ReadAsStringAsync();
        Assert.Contains("Sign in with Test provider", html);
        Assert.Contains($"form-action 'self' {provider.Authority};", Policy(signIn));

        var signedIn = await SignInThroughProviderAsync(host, returnUrl: "/ui-spike");

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/ui-spike", host.LocationOf(signedIn));
        Assert.True(host.IsSignedIn);
        Assert.Contains(">Owner</span>", await host.Client.GetStringAsync("/"));

        var rows = await AuditRowsAsync(host);
        Assert.Equal(["user.login_linked", "user.signed_in"], rows.Select(row => row.Operation));
        Assert.All(rows, row =>
        {
            Assert.Equal(InitiatorKinds.User, row.InitiatorKind);
            Assert.Equal(ownerId, row.InitiatorId);
            Assert.Equal(AdminHost.Owner.DisplayName, row.InitiatorName);
            Assert.Equal(ownerId, row.SubjectId);
        });
    }

    [Fact]
    public async Task Later_sign_ins_are_recognised_by_issuer_and_subject_even_after_the_email_changes()
    {
        await using var provider = await TestOpenIdProvider.StartAsync();
        using var install = new TemporaryInstall();
        await using var host = await StartAsync(install, provider);
        await host.CreateFirstAdministratorAsync();

        await SignInThroughProviderAsync(host);
        await host.SubmitAsync("/sign-out", "sign-out", new Dictionary<string, string>(), from: "/");
        Assert.False(host.IsSignedIn);

        provider.Identity = provider.Identity with { Email = "owner@renamed.example" };
        var again = await SignInThroughProviderAsync(host);

        Assert.Equal("/", host.LocationOf(again));
        Assert.Contains(">Owner</span>", await host.Client.GetStringAsync("/"));
        Assert.Single(await AuditRowsAsync(host), row => row.Operation == "user.login_linked");

        await using var scope = host.App.Services.CreateAsyncScope();
        var login = Assert.Single(await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().UserLogins.ToListAsync());
        Assert.Equal(provider.Authority, login.LoginProvider);
        Assert.Equal("subject-1", login.ProviderKey);
    }

    [Fact]
    public async Task An_identity_without_an_active_user_and_a_verified_email_is_refused_and_nothing_is_linked()
    {
        await using var provider = await TestOpenIdProvider.StartAsync();
        using var install = new TemporaryInstall();
        await using var host = await StartAsync(install, provider);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await host.AsUserAsync(ownerId, async directory => await directory.DisableAsync((await directory.CreateAsync(Editor)).Value));

        var identities = new[]
        {
            new ProviderIdentity("subject-unknown", "nobody@example.com"),
            new ProviderIdentity("subject-unverified", AdminHost.Owner.Email, EmailVerified: false),
            new ProviderIdentity("subject-silent", AdminHost.Owner.Email, EmailVerified: null),
            new ProviderIdentity("subject-disabled", Editor.Email),
        };

        foreach (var identity in identities)
        {
            provider.Identity = identity;
            var refused = await SignInThroughProviderAsync(host);

            Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
            Assert.Contains("data-testid=\"external-sign-in-error\"", await refused.Content.ReadAsStringAsync());
            Assert.False(host.IsSignedIn);
        }

        await using var check = host.App.Services.CreateAsyncScope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<FluentCmsDbContext>().UserLogins.ToListAsync());
        Assert.DoesNotContain(await AuditRowsAsync(host), row => row.Operation is "user.login_linked" or "user.signed_in");
    }

    [Fact]
    public async Task An_owner_can_trust_a_provider_that_never_sends_email_verified()
    {
        await using var provider = await TestOpenIdProvider.StartAsync();
        using var install = new TemporaryInstall();
        await using var host = await StartAsync(install, provider, assumeVerifiedEmail: true);
        await host.CreateFirstAdministratorAsync();
        provider.Identity = new ProviderIdentity("subject-entra", AdminHost.Owner.Email, EmailVerified: null);

        var signedIn = await SignInThroughProviderAsync(host);

        Assert.Equal("/", host.LocationOf(signedIn));
        Assert.True(host.IsSignedIn);
    }

    [Fact]
    public async Task Starting_an_external_sign_in_needs_an_antiforgery_token()
    {
        await using var provider = await TestOpenIdProvider.StartAsync();
        using var install = new TemporaryInstall();
        await using var host = await StartAsync(install, provider);
        await host.CreateFirstAdministratorAsync();

        var started = await host.Client.PostAsync(Start, new FormUrlEncodedContent(StartFields("oidc")));

        Assert.Equal(HttpStatusCode.BadRequest, started.StatusCode);
        Assert.Null(started.Headers.Location);
    }

    [Fact]
    public async Task A_provider_on_plain_http_fails_at_startup_outside_development()
    {
        await using var provider = await TestOpenIdProvider.StartAsync();
        using var install = new TemporaryInstall();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminHost.StartAsync(install, settings: ProviderSettings(provider)));

        Assert.Contains("Authentication:OpenIdConnect:Authority", failure.Message);
    }

    // The local provider speaks plain HTTP, which the host accepts only in Development; and a client that is not a browser
    // does not send Secure cookies over HTTP, so the handler's correlation and nonce cookies follow the request instead.
    private static Task<AdminHost> StartAsync(TemporaryInstall install, TestOpenIdProvider provider, bool assumeVerifiedEmail = false)
    {
        return AdminHost.StartAsync(install, ["--environment", "Development"], ProviderSettings(provider, assumeVerifiedEmail), configure: builder =>
            builder.Services.PostConfigure<OpenIdConnectOptions>("oidc", options =>
            {
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            }));
    }

    private static Dictionary<string, string?> ProviderSettings(TestOpenIdProvider provider, bool assumeVerifiedEmail = false)
    {
        return new Dictionary<string, string?>
        {
            ["Authentication:OpenIdConnect:Authority"] = provider.Authority,
            ["Authentication:OpenIdConnect:ClientId"] = TestOpenIdProvider.ClientId,
            ["Authentication:OpenIdConnect:ClientSecret"] = TestOpenIdProvider.ClientSecret,
            ["Authentication:OpenIdConnect:DisplayName"] = "Test provider",
            ["Authentication:OpenIdConnect:AssumeVerifiedEmail"] = assumeVerifiedEmail.ToString(),
        };
    }

    // Plays the browser: the start post, the provider's authorize page, its form posted back, and the admin's completion page.
    private static async Task<HttpResponseMessage> SignInThroughProviderAsync(AdminHost host, string returnUrl = "")
    {
        var started = await host.SubmitAsync(Start, null, StartFields("oidc", returnUrl), from: "/sign-in");
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);

        var authorize = await host.Client.GetStringAsync(started.Headers.Location);
        var action = WebUtility.HtmlDecode(FormAction().Match(authorize).Groups[1].Value);
        var fields = HiddenField().Matches(authorize)
            .ToDictionary(match => match.Groups[1].Value, match => WebUtility.HtmlDecode(match.Groups[2].Value));

        var callback = await host.Client.PostAsync(action, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);

        return await host.Client.GetAsync(host.LocationOf(callback));
    }

    private static Dictionary<string, string> StartFields(string provider, string returnUrl = "")
    {
        return new Dictionary<string, string> { ["provider"] = provider, ["returnUrl"] = returnUrl };
    }

    private static string Policy(HttpResponseMessage response)
    {
        return Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
    }

    private static async Task<List<Infrastructure.Audit.AuditEventRow>> AuditRowsAsync(AdminHost host)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().AuditEvents.ToListAsync();
        return rows.OrderBy(row => row.Timestamp).ToList();
    }

    [GeneratedRegex("<form method=\"post\" action=\"([^\"]+)\"")]
    private static partial Regex FormAction();

    [GeneratedRegex("<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\"")]
    private static partial Regex HiddenField();
}
