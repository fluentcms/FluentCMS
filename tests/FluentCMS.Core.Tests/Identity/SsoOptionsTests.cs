using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

public sealed class SsoOptionsTests
{
    [Fact]
    public void EachPresetBuildsItsAuthorityAndAnExplicitAuthorityWins()
    {
        new SsoProvider { Preset = SsoPreset.EntraId, TenantId = "11111111-2222-3333-4444-555555555555" }.ResolveAuthority()
            .ShouldBe("https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/v2.0");
        new SsoProvider { Preset = SsoPreset.Okta, Domain = "dev-1.okta.com" }.ResolveAuthority().ShouldBe("https://dev-1.okta.com");
        new SsoProvider { Preset = SsoPreset.Okta, Domain = "https://dev-1.okta.com/", AuthorizationServerId = "aus1" }.ResolveAuthority().ShouldBe("https://dev-1.okta.com/oauth2/aus1");
        new SsoProvider { Preset = SsoPreset.Keycloak, BaseUrl = "https://kc.example.com/auth/", Realm = "cms" }.ResolveAuthority().ShouldBe("https://kc.example.com/auth/realms/cms");
        new SsoProvider { Preset = SsoPreset.Keycloak, Authority = "https://sso.example.com/realms/x", BaseUrl = "https://kc.example.com", Realm = "cms" }.ResolveAuthority().ShouldBe("https://sso.example.com/realms/x");
    }

    [Fact]
    public void ValidProvidersOfEveryPresetPass()
    {
        var options = new SsoOptions { DisableLocalLogin = true };
        options.Providers["oidc"] = new SsoProvider { Authority = "https://id.example.com", ClientId = "cms" };
        options.Providers["entra"] = new SsoProvider { Preset = SsoPreset.EntraId, TenantId = "contoso.onmicrosoft.com", ClientId = "cms" };
        options.Providers["okta"] = new SsoProvider { Preset = SsoPreset.Okta, Domain = "dev-1.okta.com", ClientId = "cms" };
        options.Providers["key_cloak-1"] = new SsoProvider { Preset = SsoPreset.Keycloak, BaseUrl = "http://localhost:8080", Realm = "cms", ClientId = "cms", RequireHttpsMetadata = false };

        Should.NotThrow(options.Validate);
    }

    [Theory]
    [InlineData("corp/x", "Providers:corp/x")]
    [InlineData("corp.x", "Providers:corp.x")]
    public void AProviderNameMustFitAPath(string name, string key)
    {
        var options = new SsoOptions();
        options.Providers[name] = new SsoProvider { Authority = "https://id.example.com", ClientId = "cms" };

        Refused(options).Detail.ShouldStartWith(key);
    }

    [Fact]
    public void LocalLoginCannotBeDisabledWithoutAProvider() =>
        Refused(new SsoOptions { DisableLocalLogin = true }).Detail.ShouldContain("DisableLocalLogin");

    [Fact]
    public void ABadProviderIsRefusedWithItsKey()
    {
        (SsoProvider Provider, string Message)[] cases =
        [
            (new SsoProvider { Authority = "https://id.example.com" }, "ClientId is required"),
            (new SsoProvider { ClientId = "cms" }, "Authority is required"),
            (new SsoProvider { Authority = "http://id.example.com", ClientId = "cms" }, "is not an https URL"),
            (new SsoProvider { Authority = "https://id.example.com/?tenant=x", ClientId = "cms" }, "is not an https URL"),
            (new SsoProvider { Preset = SsoPreset.EntraId, ClientId = "cms" }, "TenantId is required"),
            (new SsoProvider { Preset = SsoPreset.EntraId, TenantId = "common", ClientId = "cms" }, "name one tenant"),
            (new SsoProvider { Preset = SsoPreset.EntraId, TenantId = "Organizations", ClientId = "cms" }, "name one tenant"),
            (new SsoProvider { Preset = SsoPreset.Okta, ClientId = "cms" }, "Domain is required"),
            (new SsoProvider { Preset = SsoPreset.Keycloak, BaseUrl = "https://kc.example.com", ClientId = "cms" }, "Realm are required"),
            (new SsoProvider { Preset = (SsoPreset)42, Authority = "https://id.example.com", ClientId = "cms" }, "Preset is not one of"),
            (new SsoProvider { Authority = "https://id.example.com", ClientId = "cms", GroupsClaim = " " }, "GroupsClaim must not be empty"),
            (WithMapping(new SsoRoleMapping { Group = "editors" }), "RoleMappings:0 needs a Group and a Role"),
        ];

        foreach (var (provider, message) in cases)
        {
            var options = new SsoOptions();
            options.Providers["corp"] = provider;

            var refused = Refused(options);

            refused.Detail.ShouldStartWith("Providers:corp", customMessage: message);
            refused.Detail.ShouldContain(message);
        }
    }

    private static SsoProvider WithMapping(SsoRoleMapping mapping)
    {
        var provider = new SsoProvider { Authority = "https://id.example.com", ClientId = "cms" };
        provider.RoleMappings.Add(mapping);
        return provider;
    }

    private static FluentCmsConfigurationException Refused(SsoOptions options)
    {
        var refused = Should.Throw<FluentCmsConfigurationException>(options.Validate);
        refused.Code.ShouldBe("FCMS3070");
        return refused;
    }
}
