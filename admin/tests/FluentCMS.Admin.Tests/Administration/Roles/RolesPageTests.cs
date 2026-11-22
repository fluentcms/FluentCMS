using System.Reflection;
using Bunit;
using FluentCMS.Admin.Administration.Roles;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Roles;

// The roles the caller manages: every role for roles.manage for the instance, and in a site that site's roles only, although
// GET /roles also answers the roles the caller may only read or hand out (spec 4.15).
public sealed class RolesPageTests : RolesTestContext
{
    public RolesPageTests() => UseNeutralResources();

    [Fact]
    public void ACallerWhoHoldsRolesManageForTheInstanceSeesEveryRoleWithItsSite()
    {
        var app = Open("roles");

        app.WaitForAssertion(() => Rows(app).ShouldBe(
        [
            "Super Admin|Every site|Built in",
            "Author|Every site|Built in",
            "Public|Every site|Public, read without signing in",
            "Reviewers|Main|Made by an administrator",
            "Public|Main|Public, read without signing in",
            "Shop editors|Shop|Made by an administrator",
        ]));
        app.Find("main a[href='roles/new']").TextContent.ShouldBe("New role");
    }

    [Fact]
    public void ACallerWhoHoldsRolesManageInOneSiteSeesThatSitesRolesOnly()
    {
        var app = Open("roles", inSite: ["roles.manage"], forInstance: []);

        app.WaitForAssertion(() => Rows(app).ShouldBe(["Reviewers|Main|Made by an administrator", "Public|Main|Public, read without signing in"]));
    }

    [Theory]
    [InlineData("00000000-0000-7000-8000-000000000003")]
    [InlineData("0193a1b2-0000-7000-8000-0000000000b3")]
    public void ACallerWhoHoldsRolesManageInOneSiteChangesNoRoleOfAnotherSiteOrOfEverySite(string id)
    {
        var app = Open($"roles/{id}", inSite: ["roles.manage"], forInstance: []);

        app.WaitForAssertion(() => Heading(app).ShouldBe("Role not managed here"));
        app.FindAll("main form").ShouldBeEmpty();
        Writes.ShouldBeEmpty();
    }

    // The roles of the new site, narrowed with its grants.
    [Fact]
    public void ASiteSwitchShowsTheRolesOfTheNewSite()
    {
        Grant(Shop, ["roles.manage"]);
        var app = Open("roles", inSite: ["roles.manage"], forInstance: []);
        app.WaitForAssertion(() => Rows(app).ShouldBe(["Reviewers|Main|Made by an administrator", "Public|Main|Public, read without signing in"]));

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => Rows(app).ShouldBe(["Shop editors|Shop|Made by an administrator"]));
    }

    // The list reads again on a change of the access only while it is shown: one left for another page reads nothing on a
    // later change, as of the content locale, and no longer listens to the shell. A disposed list could not read anyway,
    // its cancellation being disposed, so what listens is checked too.
    [Fact]
    public async Task AListLeftForAnotherPageDoesNotReadOnALaterChangeOfTheAccess()
    {
        Management.AddLocale(MainSite, "fr", 1);
        var app = Open("roles");
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite].DeepClone().AsObject();
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(6));
        Listeners().Count(target => target is RolesPage).ShouldBe(1);
        Navigation.NavigateTo($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Heading(app).ShouldBe("Reviewers"));
        var reads = Management.Count("roles");

        await app.InvokeAsync(() => Context.UseLocaleAsync("fr"));

        Listeners().ShouldNotContain(target => target is RolesPage);
        Navigation.NavigateTo("roles");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(6));
        Management.Count("roles").ShouldBe(reads + 1);
    }

    // The first visit of a half's route in another content locale: the router waits for the half's load after the access of
    // fr is read, and the roles page, still drawn, reads nothing for the half's address, as its search cleared (#3514). On its
    // own route a change of the access still reads again.
    [Fact]
    public async Task AListAboutToBeReplacedDoesNotReadOnAChangeOfTheAccessWhileTheRouterWaits()
    {
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([sample with { Widgets = [] }], []));
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
        Management.AddLocale(MainSite, "fr", 1);
        var app = Open("roles?q=public", inSite: ["reviews.notes.manage"]);
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite].DeepClone().AsObject();
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
        var reads = Management.Count("roles");
        var hold = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Navigation.NavigateTo("reviews?locale=fr");
        app.WaitForAssertion(() => Loader.Loads.ShouldBe(2));
        for (var turn = 0; turn < 3; turn++)
        {
            await app.InvokeAsync(() => { });
        }

        Context.ContentLocale.ShouldBe("fr");
        app.FindComponent<RouteView>().Instance.RouteData.PageType.ShouldBe(typeof(RolesPage));
        Management.Count("roles").ShouldBe(reads);
        hold.SetResult();
        app.WaitForAssertion(() => app.FindComponent<RouteView>().Instance.RouteData.PageType.ShouldNotBe(typeof(RolesPage)));
        Management.Count("roles").ShouldBe(reads);

        Navigation.NavigateTo("roles?q=public&locale=en");
        app.WaitForAssertion(() => Context.ContentLocale.ShouldBe("en"));
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
        reads = Management.Count("roles");

        Navigation.NavigateTo("roles?q=public&locale=fr");

        app.WaitForAssertion(() => Management.Count("roles").ShouldBeGreaterThan(reads));
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
    }

    // The same wait cancelled by a navigation back to the roles: the reload that waited for the list's own address reads
    // there, with the access of fr.
    [Fact]
    public async Task AReloadThatWaitedForTheListsAddressReadsOnceTheAddressIsItsOwnAgain()
    {
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([sample with { Widgets = [] }], []));
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
        Management.AddLocale(MainSite, "fr", 1);
        var app = Open("roles?q=public", inSite: ["reviews.notes.manage"]);
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite].DeepClone().AsObject();
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
        var reads = Management.Count("roles");
        var hold = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Navigation.NavigateTo("reviews?locale=fr");
        app.WaitForAssertion(() => Loader.Loads.ShouldBe(2));
        app.WaitForAssertion(() => Context.ContentLocale.ShouldBe("fr"));

        Navigation.NavigateTo("roles?q=public&locale=fr");

        app.WaitForAssertion(() => Management.Count("roles").ShouldBe(reads + 1));
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
        hold.SetResult();
        await app.InvokeAsync(() => { });
        app.FindComponent<RouteView>().Instance.RouteData.PageType.ShouldBe(typeof(RolesPage));
    }

    [Fact]
    public void TheSearchFindsRolesByName()
    {
        var app = Open("roles?q=public");

        app.WaitForAssertion(() => Rows(app).ShouldBe(["Public|Every site|Public, read without signing in", "Public|Main|Public, read without signing in"]));
    }

    [Fact]
    public void ARowOpensItsRole()
    {
        var app = Open("roles");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(6));

        var row = app.FindAll("main table tbody tr")[3];
        row.QuerySelector("a")!.GetAttribute("href").ShouldBe($"roles/{Reviewers:D}");
        row.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        app.WaitForAssertion(() => Heading(app).ShouldBe("Reviewers"));
    }

    // The target of each handler of the shell's Changed.
    private object?[] Listeners() =>
        [.. ((Delegate?)typeof(AdminContext).GetField(nameof(AdminContext.Changed), BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Context))?.GetInvocationList().Select(handler => handler.Target) ?? []];

    // Name, site and kind of each row.
    private static string[] Rows(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("main table tbody tr").Select(row => string.Join('|', row.QuerySelectorAll("td").Take(3).Select(cell => cell.TextContent.Trim())))];
}
