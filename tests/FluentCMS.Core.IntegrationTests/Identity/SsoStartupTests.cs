using System.Net;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
