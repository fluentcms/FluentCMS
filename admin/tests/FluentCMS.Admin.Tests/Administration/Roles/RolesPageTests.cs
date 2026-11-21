using System.Reflection;
using Bunit;
using FluentCMS.Admin.Administration.Roles;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components.Web;
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
