using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// The names of the admin providers of FluentCms:Sso, which are found ignoring case (#3010).
public sealed class SsoStartupTests : IAsyncDisposable
{
    private const string Providers = "FluentCms:Sso:Providers:";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

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
