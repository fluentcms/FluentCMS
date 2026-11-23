using System.Net;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// The admin providers of FluentCms:Sso as a host starts them: their names, which are found ignoring case (#3010), and the
// events a host sets after AddFluentCms (#3531).
public sealed class SsoStartupTests : IAsyncDisposable
{
    private const string Providers = "FluentCms:Sso:Providers:";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Corp => new() { [Providers + "corp:Authority"] = "https://id.example.com", [Providers + "corp:ClientId"] = "cms" };

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task AProviderAndASchemeTheHostAddsLaterWhoseNamesDifferOnlyByCaseStopTheHostWithItsCode()
    {
        var app = _host.Build(
            Environments.Production,
            new Dictionary<string, string?> { [Providers + "corp:Authority"] = "https://id.example.com", [Providers + "corp:ClientId"] = "cms" },
            builder => builder.Services.AddAuthentication().AddCookie("Corp"));

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe("FCMS3070");
        exception.Message.ShouldStartWith("FCMS3070: Providers:corp: 'corp' is the name of another authentication scheme.");
        File.Exists(_host.DatabaseFile).ShouldBeFalse();
    }

    // A delegate a host sets on a provider's events in a PostConfigure registered after AddFluentCms runs after FluentCMS's
    // check of the remote sign-out instead of replacing it (#3531): a request without the sign-in ends nothing, where the
    // handler's own sign-out would delete the sign-in cookie.
    [Fact]
    public async Task ADelegateTheHostSetsAfterAddFluentCmsKeepsTheCheckOfTheRemoteSignOut()
    {
        var ran = 0;
        var app = _host.Build(Environments.Development, Corp, builder => builder.Services.PostConfigure<OpenIdConnectOptions>("corp", options => options.Events.OnRemoteSignOut = _ =>
        {
            Interlocked.Increment(ref ran);
            return Task.CompletedTask;
        }));
        await app.StartAsync(Token);

        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        using var answer = await client.GetAsync(new Uri("/account/sso/corp/signout?sid=s1&iss=https%3A%2F%2Fid.example.com", UriKind.Relative), Token);

        answer.StatusCode.ShouldBe(HttpStatusCode.OK);
        answer.Headers.CacheControl!.NoStore.ShouldBeTrue();
        answer.Headers.Contains("Set-Cookie").ShouldBeFalse();
        Volatile.Read(ref ran).ShouldBe(1);
    }

    // So does the callback on the public origin, in either pool: a delegate of OnRedirectToIdentityProvider set there runs
    // after it, and the redirect_uri is not built from the request (#3531).
    [Theory]
    [InlineData("corp", "/account/sso/corp")]
    [InlineData("member-corp", "/account/members/sso/corp")]
    public async Task ADelegateTheHostSetsAfterAddFluentCmsKeepsTheCallbackOnThePublicOrigin(string scheme, string callback)
    {
        var ran = 0;
        var settings = new Dictionary<string, string?>(Corp)
        {
            ["FluentCms:PublicOrigin"] = "https://cms.example.com",
            ["FluentCms:Members:Providers:corp:Type"] = "Oidc",
            ["FluentCms:Members:Providers:corp:ClientId"] = "corp-id",
            ["FluentCms:Members:Providers:corp:Authority"] = "https://id.example.com/",
        };
        var app = _host.Build(
            Environments.Development,
            settings,
            builder => builder.Services.PostConfigure<OpenIdConnectOptions>(scheme, options =>
            {
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration { AuthorizationEndpoint = "https://id.example.com/authorize" });
                options.Events.OnRedirectToIdentityProvider = _ =>
                {
                    Interlocked.Increment(ref ran);
                    return Task.CompletedTask;
                };
            }),
            pipeline: built =>
            {
                built.UseFluentCms();
                built.MapGet("/test/challenge", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [scheme]));
            });
        await app.StartAsync(Token);

        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        using var challenge = await client.GetAsync(new Uri("/test/challenge", UriKind.Relative), Token);

        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var authorize = challenge.Headers.Location.ShouldNotBeNull();
        authorize.GetLeftPart(UriPartial.Path).ShouldBe("https://id.example.com/authorize");
        QueryHelpers.ParseQuery(authorize.Query)["redirect_uri"].ToString().ShouldBe("https://cms.example.com" + callback);
        Volatile.Read(ref ran).ShouldBe(1);
    }

    // Events or an EventsType a host sets there would drop FluentCMS's events altogether, so the host does not start (#3531).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EventsTheHostReplacesAfterAddFluentCmsStopTheHostWithItsCode(bool asType)
    {
        var app = _host.Build(Environments.Production, Corp, builder => builder.Services.PostConfigure<OpenIdConnectOptions>("corp", options =>
        {
            if (asType)
            {
                options.EventsType = typeof(OpenIdConnectEvents);
            }
            else
            {
                options.Events = new OpenIdConnectEvents();
            }
        }));

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe("FCMS3070");
        exception.Message.ShouldStartWith("FCMS3070: Providers:corp: the Events or EventsType of the provider's OpenIdConnectOptions were replaced");
        File.Exists(_host.DatabaseFile).ShouldBeFalse();
    }

    // A host that adds an OpenID Connect scheme of its own, as AddOpenIdConnect and Microsoft.Identity.Web do, before or
    // after AddFluentCms, starts, with or without providers of FluentCMS; theirs keep FluentCMS's events, and the host's
    // scheme keeps its own. The handler's registration reads the authentication options when its options are built, so
    // the check of the events must not build them while the authentication options are being built (#3531).
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AHostWithAnOpenIdConnectSchemeOfItsOwnStarts(bool before, bool providers)
    {
        var hostEvents = new OpenIdConnectEvents();
        Action<WebApplicationBuilder> other = builder => builder.Services.AddAuthentication().AddOpenIdConnect("other", options =>
        {
            options.Authority = "https://other.example.com";
            options.ClientId = "other";
            options.CallbackPath = "/signin-other";
            options.Events = hostEvents;
        });
        var settings = providers
            ? new Dictionary<string, string?>(Corp)
            {
                ["FluentCms:Members:Providers:corp:Type"] = "Oidc",
                ["FluentCms:Members:Providers:corp:ClientId"] = "corp-id",
                ["FluentCms:Members:Providers:corp:Authority"] = "https://id.example.com/",
            }
            : [];
        // A host of its own, left undisposed if it hangs, since its disposal would wait on the start as well.
        var own = new CmsWebHost();
        var app = own.Build(Environments.Development, settings, before ? null : other, before: before ? other : null);
        var started = Task.Run(() => app.StartAsync(Token), Token);
        (await Task.WhenAny(started, Task.Delay(TimeSpan.FromSeconds(45), Token))).ShouldBe(started, "the host did not start");
        await using var disposed = own;
        await started;

        var handlers = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        handlers.Get("other").Events.ShouldBeSameAs(hostEvents);
        handlers.Get("other").CallbackPath.Value.ShouldBe("/signin-other");
        if (!providers)
        {
            return;
        }

        handlers.Get("corp").Events.ShouldBeAssignableTo<ChainedOpenIdConnectEvents>();
        handlers.Get("member-corp").Events.ShouldBeAssignableTo<ChainedOpenIdConnectEvents>();
        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        using (var signOut = await client.GetAsync(new Uri("/account/sso/corp/signout?sid=s1&iss=https%3A%2F%2Fid.example.com", UriKind.Relative), Token))
        {
            signOut.StatusCode.ShouldBe(HttpStatusCode.OK);
            signOut.Headers.Contains("Set-Cookie").ShouldBeFalse();
        }

        using var member = await client.GetAsync(new Uri("/account/members/sso/corp/signout", UriKind.Relative), Token);
        member.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NamesInConfigurationThatDifferOnlyByCaseAreOneProvider()
    {
        var app = _host.Build(Environments.Development, new Dictionary<string, string?>
        {
            [Providers + "Corp:Authority"] = "https://id.example.com",
            [Providers + "Corp:ClientId"] = "cms",
            [Providers + "corp:ClientSecret"] = "secret",
        });
        await app.StartAsync(Token);

        var provider = app.Services.GetRequiredService<SsoOptions>().Providers.ShouldHaveSingleItem();
        provider.Key.ShouldBe("Corp", StringCompareShould.IgnoreCase);
        (provider.Value.ClientId, provider.Value.ClientSecret).ShouldBe(("cms", "secret"));
        (await app.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync()).Count(scheme => string.Equals(scheme.Name, "corp", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
    }
}
