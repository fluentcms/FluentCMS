using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Common;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Common;

// The read of GET /roles and the select the users, keys and members pages offer the roles in.
public sealed class RoleSelectTests : AdminTestContext
{
    private static readonly Guid Editor = new("0193a1b2-0000-7000-8000-0000000000e1");
    private static readonly Guid Author = new("0193a1b2-0000-7000-8000-0000000000a1");

    private JsonArray roles =
    [
        new JsonObject { ["id"] = Editor, ["siteId"] = null, ["name"] = "Editor", ["description"] = "Edits", ["builtIn"] = true, ["requireTwoFactor"] = false, ["grants"] = new JsonArray() },
        new JsonObject { ["id"] = Author, ["siteId"] = MainSite, ["name"] = "Author", ["description"] = null, ["builtIn"] = false, ["requireTwoFactor"] = false, ["grants"] = new JsonArray() },
    ];

    private bool refused;

    private static JsonObject Role(string id, Guid? site, string name) =>
        new() { ["id"] = id, ["siteId"] = site, ["name"] = name, ["description"] = null, ["builtIn"] = false, ["requireTwoFactor"] = false, ["grants"] = new JsonArray() };

    public RoleSelectTests()
    {
        UseNeutralResources();
        Management.Answer = (method, path) => path != "roles" ? null
            : refused ? Problem(HttpStatusCode.Forbidden, "FCMS3103")
            : Json(new JsonObject { ["items"] = roles.DeepClone() });
        Start();
    }

    [Fact]
    public void ItReadsTheRolesOnceAndOffersThemByName()
    {
        var select = Render<RoleSelect>(parameters => parameters.Add(item => item.Label, "Role"));

        select.WaitForAssertion(() => select.FindAll("option").Select(option => option.TextContent).ShouldBe(["Choose a role", "Editor", $"Author ({MainSite:D})"]));
        select.Find("label").TextContent.ShouldContain("Role");
        select.Render();
        Management.Count("roles").ShouldBe(1);
    }

    [Fact]
    public void AChoiceRaisesTheIdOfTheRoleAndNoneRaisesNull()
    {
        var chosen = new List<Guid?>();
        var select = Render<RoleSelect>(parameters => parameters
            .Add(item => item.Label, "Role")
            .Add(item => item.ValueChanged, id => chosen.Add(id)));
        select.WaitForAssertion(() => select.Find("select"));

        select.Find("select").Change(Author.ToString("D"));
        select.Find("select").Change("");

        chosen.ShouldBe([Author, null]);
    }

    [Fact]
    public void ThePageNarrowsTheRolesOffered()
    {
        var select = Render<RoleSelect>(parameters => parameters
            .Add(item => item.Label, "Role")
            .Add(item => item.Value, Editor)
            .Add(item => item.Offers, role => role.SiteId is null));

        select.WaitForAssertion(() => select.FindAll("option").Select(option => option.TextContent).ShouldBe(["Choose a role", "Editor"]));
        select.Find("select").GetAttribute("value").ShouldBe(Editor.ToString("D"));
    }

    // A name is unique among the roles of one site only, and GET /roles gives a caller with the grant for the instance
    // the roles of every site.
    [Fact]
    public async Task RolesOfOneNameAreToldApartByTheirSite()
    {
        var shop = new Guid("0193a1b2-0000-7000-8000-00000000a002");
        var elsewhere = new Guid("0193a1b2-0000-7000-8000-00000000a003");
        Management.AddSite(shop, "Shop", "shop");
        roles =
        [
            Role("0193a1b2-0000-7000-8000-0000000000b1", null, "Author"),
            Role("0193a1b2-0000-7000-8000-0000000000b2", MainSite, "Author"),
            Role("0193a1b2-0000-7000-8000-0000000000b3", shop, "Author"),
            Role("0193a1b2-0000-7000-8000-0000000000b4", elsewhere, "Author"),
        ];
        await Services.GetRequiredService<FluentCMS.Admin.Shell.AdminContext>().StartAsync();

        var select = Render<RoleSelect>(parameters => parameters.Add(item => item.Label, "Role"));

        select.WaitForAssertion(() => select.FindAll("option").Select(option => option.TextContent)
            .ShouldBe(["Choose a role", "Author", "Author (Main)", "Author (Shop)", $"Author ({elsewhere:D})"]));
    }

    [Fact]
    public void RolesThePageHasReadAreOfferedWithoutARead()
    {
        var select = Render<RoleSelect>(parameters => parameters
            .Add(item => item.Label, "Role")
            .Add(item => item.Roles, [new RoleSummary { Id = Author, Name = "Author" }]));

        select.FindAll("option").Select(option => option.TextContent).ShouldBe(["Choose a role", "Author"]);
        Management.Count("roles").ShouldBe(0);
    }

    [Fact]
    public void ARefusedReadSaysSoAndTheRetryReadsAgain()
    {
        refused = true;
        var select = Render<RoleSelect>(parameters => parameters.Add(item => item.Label, "Role"));
        select.WaitForAssertion(() => select.Find("[role=alert]").TextContent.ShouldContain("The roles could not be read."));
        refused = false;

        select.Find("fluent-button").Click();

        select.WaitForAssertion(() => select.FindAll("option").Count.ShouldBe(3));
        Management.Count("roles").ShouldBe(2);
    }
}
