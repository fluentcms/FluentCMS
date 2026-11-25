using System.Reflection;
using Bunit;
using FluentCMS.Admin.Administration.Gallery;
using Microsoft.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Administration;

// The sweep of spec 7 over the administration assembly: each page is swept by the localization tests of its area, with the
// answers its page reads, the account page by AccountLocalizationTests, and the gallery here.
public sealed class AdministrationLocalizationTests : AdministrationTestContext
{
    // The route of each page and the tests that sweep it; a new page is swept in its area's tests and named here.
    private static readonly Dictionary<string, string> Swept = new(StringComparer.Ordinal)
    {
        ["/account"] = "AccountLocalizationTests",
        ["/api-keys"] = "ApiKeysLocalizationTests",
        ["/api-keys/new"] = "ApiKeysLocalizationTests",
        ["/api-keys/{Id:guid}"] = "ApiKeysLocalizationTests",
        ["/system/diagnostics"] = "DiagnosticsLocalizationTests",
        ["/system/components"] = nameof(AdministrationLocalizationTests),
        ["/jobs"] = "JobsLocalizationTests",
        ["/jobs/{Id:guid}"] = "JobsLocalizationTests",
        ["/members"] = "MembersLocalizationTests",
        ["/members/groups"] = "MembersLocalizationTests",
        ["/members/{Id:guid}"] = "MembersLocalizationTests",
        ["/plugins"] = "PluginsLocalizationTests",
        ["/roles"] = "RolesLocalizationTests",
        ["/roles/new"] = "RolesLocalizationTests",
        ["/roles/{Id:guid}"] = "RolesLocalizationTests",
        ["/settings"] = "SettingsLocalizationTests",
        ["/settings/{Area}"] = "SettingsLocalizationTests",
        ["/settings/{Area}/defaults"] = "SettingsLocalizationTests",
        ["/sites"] = "SitesLocalizationTests",
        ["/users"] = "UsersLocalizationTests",
        ["/users/{Id:guid}"] = "UsersLocalizationTests",
    };

    [Fact]
    public void EveryPageOfTheAdministrationAssemblyIsSwept()
    {
        PseudoSweep.RoutablePages(typeof(GalleryResources).Assembly)
            .SelectMany(page => page.GetCustomAttributes<RouteAttribute>())
            .Select(route => route.Template)
            .ShouldBe(Swept.Keys, ignoreOrder: true);
        Swept.Values.Distinct().Where(name => typeof(AdministrationLocalizationTests).Assembly.GetTypes().All(type => type.Name != name)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheGalleryComesFromTheResources(string culture) =>
        LoadLocalized(culture, "system/components", app => app.FindAll("main section").Count.ShouldBeGreaterThan(3));
}
