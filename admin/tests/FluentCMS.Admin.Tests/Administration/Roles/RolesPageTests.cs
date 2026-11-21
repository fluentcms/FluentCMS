using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;
using Xunit;

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

    // Name, site and kind of each row.
    private static string[] Rows(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("main table tbody tr").Select(row => string.Join('|', row.QuerySelectorAll("td").Take(3).Select(cell => cell.TextContent.Trim())))];
}
