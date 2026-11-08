using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.IntegrationTests.Hosting;
using FluentCMS.Core.Identity;
using FluentCMS.Events;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

public sealed partial class HostedPagesTests
{
    private static readonly string[] Routes =
    [
        "account/accept-invitation",
        "account/change-password",
        "account/consent",
        "account/enable-two-factor",
        "account/error",
        "account/external-login",
        "account/forgot-password",
        "account/login",
        "account/logout",
        "account/recovery-code",
        "account/reset-password",
        "account/two-factor",
    ];

    [Fact]
    public void EveryHostedPageCarriesTheAccountConventions()
    {
        var pages = _host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<PageActionDescriptor>()?.AreaName == "FluentCms")
            .ToList();

        pages.Select(page => page.RoutePattern.RawText).Order().ShouldBe(Routes);
        foreach (var page in pages)
        {
            page.Metadata.GetMetadata<HostedPageMetadata>().ShouldNotBeNull(page.DisplayName);
            page.Metadata.GetMetadata<FluentCmsEndpointMetadata>().ShouldNotBeNull(page.DisplayName);
            page.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(RateLimitPolicyNames.Auth, page.DisplayName);
            page.Metadata.GetMetadata<FluentCmsSurfaceMetadata>()!.Name.ShouldBe("account", page.DisplayName);
        }
    }

    [Fact]
    public async Task EveryHostedPageAnswersWithFrameAncestorsAndAPolicyThatRunsNoScript()
    {
        using var browser = new HostedPagesBrowser(_host);

        foreach (var route in Routes)
        {
            using var response = await browser.GetAsync("/" + route);
            response.StatusCode.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.Redirect, HttpStatusCode.BadRequest], route);
            var policy = string.Join(", ", response.Headers.GetValues("Content-Security-Policy"));
            policy.ShouldContain("frame-ancestors 'none'", customMessage: route);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                policy.ShouldContain("default-src 'none'", customMessage: route);
            }

            response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"], route);
        }
    }

    [Fact]
    public async Task TheHostedPagesAreRateLimitedByClientAddress()
    {
        await using var host = NewHost(new CapturedEmails()).WithServices(services =>
            services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 3));
        using var flooding = new HostedPagesBrowser(host, "10.9.0.1");
        using var other = new HostedPagesBrowser(host, "10.9.0.2");

        foreach (var route in new[] { "/account/login", "/account/forgot-password", "/account/error" })
        {
            using var allowed = await flooding.GetAsync(route);
            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, route);
        }

        using var refused = await flooding.GetAsync("/account/login");
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        using var elsewhere = await other.GetAsync("/account/login");
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AHostCanOverrideAPageAndKeepItsModel()
    {
        await using var host = NewHost(new CapturedEmails()).WithServices(services =>
            services.AddRazorPages().ConfigureApplicationPartManager(manager => manager.ApplicationParts.Insert(0, new HostPagesPart())));
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host);

        using (var page = await browser.GetAsync("/account/login"))
        {
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            var html = await page.Content.ReadAsStringAsync(Token);
            // In the area's layout, which its _ViewStart gives the host's page too.
            html.ShouldContain("<main>");
            html.ShouldContain("<p>The host's own sign-in page</p>");
            html.ShouldNotContain("User name or email");
            string.Join(", ", page.Headers.GetValues("Content-Security-Policy")).ShouldContain("frame-ancestors 'none'");
        }

        // Its form posts to the same model, which signs the user in; the other pages stay FluentCMS's.
        using var signedIn = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password));
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeTrue();
        (await browser.PageAsync("/account/forgot-password")).ShouldContain("Forgot your password?");
    }

    [Fact]
    public async Task AHostThatMapsItsOwnRazorPagesGetsEachHostedPageOnce()
    {
        await using var web = new CmsWebHost();
        var app = web.Build("Development", pipeline: app =>
        {
            app.UseFluentCms();
            app.MapRazorPages();
        });
        await app.StartAsync(Token);
        using var client = app.GetTestClient();
        client.BaseAddress = HostedPagesBrowser.OAuthBase;

        using var response = await client.GetAsync("/account/login", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Count(endpoint => endpoint.RoutePattern.RawText == "account/login").ShouldBe(1);
    }

    [Fact]
    public async Task AUserLinkedToAnExternalLoginSignsInThroughTheCallbackPage()
    {
        await using var host = NewHost(new CapturedEmails()).WithServices(services =>
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, FakeSsoHandler>(FakeSsoHandler.Name, "Test SSO", configureOptions: null));
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host);

        var login = await browser.PageAsync("/account/login");
        login.ShouldContain("Sign in with Test SSO");

        // Not linked yet: the callback page says so and nothing signs in.
        using (var unlinked = await browser.PostAsync("/account/login?handler=External", login, new Dictionary<string, string> { ["provider"] = FakeSsoHandler.Name }))
        {
            unlinked.Headers.Location!.OriginalString.ShouldStartWith("/account/external-login?");
            using var callback = await browser.GetAsync(unlinked.Headers.Location.OriginalString);
            callback.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await callback.Content.ReadAsStringAsync(Token)).ShouldContain("is not linked to a user here");
            browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.AddLoginAsync((await users.FindByIdAsync(editor.Id.ToString()))!, new UserLoginInfo(FakeSsoHandler.Name, FakeSsoHandler.Subject, "Test SSO"))).Succeeded.ShouldBeTrue();
        }

        using (var linked = await browser.PostAsync("/account/login?handler=External", login, new Dictionary<string, string> { ["provider"] = FakeSsoHandler.Name }))
        {
            using var callback = await browser.GetAsync(linked.Headers.Location!.OriginalString);
            callback.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            callback.Headers.Location!.OriginalString.ShouldBe("/");
            browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeTrue();
        }

        using (var unknown = await browser.PostAsync("/account/login?handler=External", login, new Dictionary<string, string> { ["provider"] = IdentityConstants.ApplicationScheme }))
        {
            unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // Each action is awaited on its own: user.login_linked shares the prefix, and the rows arrive in any order.
        Details((await ActivityAsync(host, "user.login_failed", count: 1)).Single())["reason"].ShouldBe(LoginFailureReasons.UnknownUser);
        Details((await ActivityAsync(host, "user.login_succeeded", count: 1)).Single())["method"].ShouldBe(LoginMethods.External);
    }

    [Fact]
    public async Task AFailureOnAHostedPageAnswersWithAPageNotAProblemDocument()
    {
        await using var host = NewHost(new CapturedEmails()).WithServices(services =>
            services.AddScoped<ISettings<BrandingSettings>, FailingBranding>());
        using var browser = new HostedPagesBrowser(host);

        using var page = await browser.GetAsync("/account/login");

        page.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        page.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var html = await page.Content.ReadAsStringAsync(Token);
        html.ShouldContain("<h1>Something went wrong</h1>");
        html.ShouldNotContain("branding is down");
        string.Join(", ", page.Headers.GetValues("Content-Security-Policy")).ShouldContain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task UnderAPathBaseThePagesAndTheirLinksCarryIt()
    {
        var emails = new CapturedEmails();
        await using var host = NewHost(emails).Configure(cms => cms.ReverseProxy.PathBase = "/cms");
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host);
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);

        using var challenge = await browser.GetAsync("/cms" + request.Url);
        challenge.Headers.Location!.AbsolutePath.ShouldBe("/cms/account/login");
        using var signedIn = await browser.SubmitAsync(challenge.Headers.Location.PathAndQuery, Credentials(editor.Email, Password));
        signedIn.Headers.Location!.OriginalString.ShouldStartWith("/cms/connect/authorize?");
        using var done = await browser.FollowAsync(signedIn);
        done.Headers.Location!.AbsoluteUri.ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");

        using var outside = await browser.GetAsync("/account/login");
        outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var logout = await browser.GetAsync("/cms/connect/logout");
        logout.Headers.Location!.OriginalString.ShouldBe("/cms/account/logout");
        (await browser.PageAsync("/cms/account/logout")).ShouldContain("action=\"/cms/connect/logout\"");

        using var sent = await browser.SubmitAsync("/cms/account/forgot-password", new Dictionary<string, string> { ["Input.Email"] = editor.Email });
        emails.LinkTo(editor.Email).AbsolutePath.ShouldBe("/cms/account/reset-password");
    }

    [Fact]
    public async Task TheExternalCallbackWithoutAnExternalSignInGoesBackToTheLoginPage()
    {
        using var browser = new HostedPagesBrowser(_host);

        using var response = await browser.GetAsync("/account/external-login");

        response.Headers.Location!.OriginalString.ShouldBe("/account/login?ReturnUrl=%2F&Status=external-failed");
    }

    private sealed class FailingBranding : ISettings<BrandingSettings>
    {
        public ValueTask<BrandingSettings> GetAsync(SiteId site, CancellationToken cancellationToken = default) => throw Down();

        public ValueTask<BrandingSettings> GetDefaultAsync(CancellationToken cancellationToken = default) => throw Down();

        public ValueTask SetAsync(SiteId site, BrandingSettings value, CancellationToken cancellationToken = default) => throw Down();

        public ValueTask SetDefaultAsync(BrandingSettings value, CancellationToken cancellationToken = default) => throw Down();

        public ValueTask ResetAsync(SiteId site, CancellationToken cancellationToken = default) => throw Down();

        public ValueTask ResetDefaultAsync(CancellationToken cancellationToken = default) => throw Down();

        private static InvalidOperationException Down() => new("The branding is down.");
    }

    // A remote provider that signs the user in at once: the external cookie, then back to the callback.
    private sealed class FakeSsoHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "TestSso";
        public const string Subject = "sso-user-1";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Subject)], Name);
            await Context.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity), properties);
            Context.Response.Redirect(properties.RedirectUri!);
        }
    }

    // What a host's own Areas/FluentCms/Pages/Account/Login.cshtml compiles to, written out: a page at the same path, on
    // the hosted login model, in an application part ahead of FluentCMS's.
    [RazorCompiledItemMetadata("RouteTemplate", "/account/login")]
    private sealed class HostLoginPage : Page
    {
        public ViewDataDictionary<LoginModel> ViewData => (ViewDataDictionary<LoginModel>)PageContext.ViewData;

        public LoginModel Model => ViewData.Model;

        public override Task ExecuteAsync()
        {
            var tokens = HttpContext.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(HttpContext);
            WriteLiteral($"<p>The host's own sign-in page</p><form method=\"post\"><input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"{tokens.RequestToken}\"></form>");
            return Task.CompletedTask;
        }
    }

    private sealed class HostPagesPart : ApplicationPart, IRazorCompiledItemProvider
    {
        public override string Name => "host pages";

        public IEnumerable<RazorCompiledItem> CompiledItems { get; } = [new Item()];

        private sealed class Item : RazorCompiledItem
        {
            public override string Identifier => "/Areas/FluentCms/Pages/Account/Login.cshtml";

            public override string Kind => "mvc.1.0.razor-page";

            public override IReadOnlyList<object> Metadata => typeof(HostLoginPage).GetCustomAttributes(inherit: true);

            public override Type Type => typeof(HostLoginPage);
        }
    }
}
