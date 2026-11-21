using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Roles;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Roles;

// The editor of a role (spec 4.15): the grants from the catalogue, the round trip of the constraints it does not edit, the
// marks of what the caller does not hold and the server's refusals by their code.
public sealed class RolePageTests : RolesTestContext
{
    public RolePageTests() => UseNeutralResources();

    [Fact]
    public void TheCatalogueIsOfferedBySourceWithThePluginBehindAPermissionAndWhatTheCallerDoesNotHoldMarked()
    {
        var app = Open("roles/new");
        app.WaitForAssertion(() => Field(app, "Kind of permission"));

        Options(Field(app, "Kind of permission")).ShouldBe(["Core", "Content types", "Plugins", "Settings"]);
        Options(Field(app, "Permission or pattern")).ShouldBe(
        [
            "Choose a permission",
            "*: Every permission, also those of plugins and content types added later. (not among your permissions)",
            "users.manage: Invite, edit and deactivate users. (not among your permissions)",
            "roles.manage: Create and edit roles and their grants.",
            "schema.read: Read the content schema.",
        ]);

        Field(app, "Kind of permission").Change(GrantCatalogue.Content);
        app.WaitForAssertion(() => Options(Field(app, "Permission or pattern")).ShouldBe(
        [
            "Choose a permission",
            "content.*.create: Matches 1 permission of the catalogue.",
            "content.*.read: Matches 1 permission of the catalogue.",
            "content.*.restore: Matches 1 permission of the catalogue. (not among your permissions)",
            "content.article.*: Matches 3 permissions of the catalogue. (not among your permissions)",
            "content.article.create: Create article entries.",
            "content.article.read: Read article entries.",
            "content.article.restore: Restore article entries from the trash. (not among your permissions)",
        ]));

        Field(app, "Kind of permission").Change(GrantCatalogue.Plugin);
        app.WaitForAssertion(() => Options(Field(app, "Permission or pattern")).ShouldBe(["Choose a permission", "media.read: Read media. (plugin media)"]));
    }

    [Fact]
    public void AnAdministratorMakesARoleWithGrantsOfCoreOfAContentTypeAndOfAPluginChangesItAndDeletesIt()
    {
        var app = Open("roles/new");
        app.WaitForAssertion(() => Field(app, "Name"));

        Field(app, "Name").Input("Translators");
        Add(app, GrantCatalogue.Core, "schema.read");
        Add(app, GrantCatalogue.Content, "content.article.read");
        Add(app, GrantCatalogue.Plugin, "media.read");
        app.WaitForAssertion(() => GrantsShown(app).ShouldBe(["schema.read", "content.article.read", "media.read"]));
        app.Find("main [data-grant='media.read']").TextContent.ShouldContain("Plugin: media");
        Save(app);

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith("/roles/0193a1b2-0000-7000-8000-000000000001"));
        var created = LastWrite(HttpMethod.Post);
        created["name"]!.GetValue<string>().ShouldBe("Translators");
        created["siteId"]!.GetValue<string>().ShouldBe(MainSite.ToString("D"));
        created["requireTwoFactor"]!.GetValue<bool>().ShouldBeFalse();
        JsonNode.DeepEquals(created["grants"], new JsonArray(
            new JsonObject { ["permission"] = "schema.read" },
            new JsonObject { ["permission"] = "content.article.read" },
            new JsonObject { ["permission"] = "media.read" })).ShouldBeTrue(created["grants"]!.ToJsonString());
        app.WaitForAssertion(() => Heading(app).ShouldBe("Translators"));

        Field(app, "Name").Input("Translators of articles");
        Save(app);
        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        LastWrite(HttpMethod.Put)["name"]!.GetValue<string>().ShouldBe("Translators of articles");

        Button(app, "Delete the role").Click();
        app.WaitForAssertion(() => Button(app, "Delete").Click());

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith("/roles"));
        Writes.Last().Method.ShouldBe(HttpMethod.Delete);
        Roles.ShouldNotContain(role => role!["name"]!.GetValue<string>() == "Translators of articles");
    }

    // A save replaces every grant, so each one goes back with every constraint as it was read, resources included.
    [Fact]
    public void ARoleWithConstrainedGrantsIsSavedWithItsNameChangedAndKeepsEveryConstraint()
    {
        var read = RoleOf(Reviewers)["grants"]!.DeepClone().AsArray();
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Field(app, "Name"));
        app.Find("main [data-grant='content.article.read']").TextContent.ShouldContain("folder-1, folder-2");
        app.Find("main [data-grant='content.article.read']").TextContent.ShouldContain("own-entries");

        Field(app, "Name").Input("Article reviewers");
        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        var sent = LastWrite(HttpMethod.Put);
        sent["name"]!.GetValue<string>().ShouldBe("Article reviewers");
        JsonNode.DeepEquals(sent["grants"], Sent(read)).ShouldBeTrue(sent["grants"]!.ToJsonString());
        JsonNode.DeepEquals(RoleOf(Reviewers)["grants"], read).ShouldBeTrue();
    }

    [Fact]
    public void AGrantTheCallerDoesNotHoldIsMarkedAndTheServersRefusalIsShownOnTheRole()
    {
        Refuse = (method, path) => method == HttpMethod.Put
            ? Answer(HttpStatusCode.Forbidden, "FCMS3103", "The role 'Reviewers' grants more than the caller holds, so the caller may not make, change or hand it out.")
            : null;
        var app = Open($"roles/{Reviewers:D}", inSite: ["roles.manage"], forInstance: []);
        app.WaitForAssertion(() => Field(app, "Name"));

        Marked(app).ShouldBe(["media.read"]);
        Add(app, GrantCatalogue.Core, "users.manage");
        app.WaitForAssertion(() => Marked(app).ShouldBe(["media.read", "users.manage"]));
        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldStartWith("Reviewers grants more than your roles hold, before or after this change, so you may not make, change or delete it."));
        Alert(app).ShouldEndWith("The role 'Reviewers' grants more than the caller holds, so the caller may not make, change or hand it out.");
        Field(app, "Name").GetAttribute("value").ShouldBe("Reviewers");
        GrantsShown(app).ShouldContain("users.manage");
    }

    [Theory]
    [InlineData("PUT", "FCMS3043", "A built-in role cannot be deleted, and of Super Admin only whether its holders sign in with a second factor can be changed.")]
    [InlineData("PUT", "FCMS3052", "This change would leave no active Super Admin, so it was refused.")]
    [InlineData("DELETE", "FCMS3044", "This role is still held by users, by API keys that are not revoked or by member groups. Take it from them first.")]
    [InlineData("DELETE", "FCMS3043", "A built-in role cannot be deleted, and of Super Admin only whether its holders sign in with a second factor can be changed.")]
    public void ARefusalIsShownByItsCode(string method, string code, string shown)
    {
        Refuse = (sent, path) => sent.Method == method ? Problem(HttpStatusCode.Conflict, code, "id") : null;
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Field(app, "Name"));

        if (method == "PUT")
        {
            Save(app);
        }
        else
        {
            Button(app, "Delete the role").Click();
            app.WaitForAssertion(() => Button(app, "Delete").Click());
        }

        app.WaitForAssertion(() => Alert(app).ShouldBe(shown));
        Navigation.Uri.ShouldEndWith($"/roles/{Reviewers:D}");
        Roles.ShouldContain(role => (Guid)role!["id"]! == Reviewers);
    }

    [Fact]
    public void AGrantTheServerRefusesIsMarkedOnItsRow()
    {
        Refuse = (method, path) => method == HttpMethod.Put ? Problem(HttpStatusCode.BadRequest, "FCMS3042", "grants[1].constraints.locales") : null;
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Field(app, "Name"));

        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was not saved: the server refused the grants marked."));
        app.FindAll("main [data-grant] [role=alert]").Select(alert => alert.ParentElement!.GetAttribute("data-grant")).ShouldBe(["content.article.read"]);
    }

    // A role has no ETag: another change saved meanwhile is refused with FCMS3045, and the role is read again with what the
    // user changed put back on it.
    [Fact]
    public void AConcurrentChangeReadsTheRoleAgainWithoutDroppingWhatTheUserTyped()
    {
        var refusals = 1;
        Refuse = (method, path) => method == HttpMethod.Put && refusals-- > 0 ? Problem(HttpStatusCode.Conflict, "FCMS3045", "id") : null;
        var app = Open($"roles/{Reviewers:D}");
        app.WaitForAssertion(() => Field(app, "Name"));

        Field(app, "Name").Input("Mine");
        Button(app, "Remove the grant schema.read").Click();
        app.WaitForAssertion(() => GrantsShown(app).ShouldBe(["content.article.read", "media.read"]));
        RoleOf(Reviewers)["description"] = "Theirs";
        RoleOf(Reviewers)["grants"]!.AsArray().Add(Grant("content.article.create", new JsonObject { ["locales"] = new JsonArray("fr") }));
        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe(
            "Someone saved a change of this role while you edited it. The role was read again, with your changes kept on it: check it and save again."));
        Field(app, "Name").GetAttribute("value").ShouldBe("Mine");
        Field(app, "Description").GetAttribute("value").ShouldBe("Theirs");
        GrantsShown(app).ShouldBe(["content.article.read", "media.read", "content.article.create"]);

        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        var sent = LastWrite(HttpMethod.Put);
        sent["name"]!.GetValue<string>().ShouldBe("Mine");
        sent["description"]!.GetValue<string>().ShouldBe("Theirs");
        JsonNode.DeepEquals(sent["grants"], Sent(new JsonArray(
            RoleOf(Reviewers)["grants"]![0]!.DeepClone(),
            RoleOf(Reviewers)["grants"]![1]!.DeepClone(),
            Grant("content.article.create", new JsonObject { ["locales"] = new JsonArray("fr") })))).ShouldBeTrue(sent["grants"]!.ToJsonString());
    }

    [Theory]
    [InlineData("0193a1b2-0000-7000-8000-0000000000b2", "This is the Public role of the site: anyone who has not signed in reads what it grants, through the Delivery API.")]
    [InlineData("00000000-0000-7000-8000-000000000004", "This is the Public role of every site without one of its own: anyone who has not signed in reads what it grants there, through the Delivery API.")]
    public void ThePublicRoleIsEditedUnderItsNotice(string id, string notice)
    {
        var app = Open($"roles/{id}");
        app.WaitForAssertion(() => Field(app, "Name"));

        app.Find("main [role=note]").TextContent.Trim().ShouldBe(notice);
        Field(app, "Name").HasAttribute("readonly").ShouldBeTrue();
        Add(app, GrantCatalogue.Content, "content.article.read");
        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldBe("The role was saved."));
        JsonNode.DeepEquals(LastWrite(HttpMethod.Put)["grants"], new JsonArray(new JsonObject { ["permission"] = "content.article.read" })).ShouldBeTrue();
        app.FindAll("main fluent-button").ShouldNotContain(button => button.TextContent.Trim() == "Delete the role");
    }

    [Fact]
    public void SuperAdminChangesItsSecondFactorOnly()
    {
        var app = Open($"roles/{SuperAdmin:D}");
        app.WaitForAssertion(() => Field(app, "Name"));

        Field(app, "Description").HasAttribute("readonly").ShouldBeTrue();
        app.FindAll("main select").ShouldBeEmpty();
        app.FindAll("main fluent-button").Select(button => button.TextContent.Trim()).ShouldBe(["Save"]);
    }

    private static string[] Marked(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("main [data-grant]").Where(row => row.TextContent.Contains("Not among your permissions", StringComparison.Ordinal)).Select(row => row.GetAttribute("data-grant")!)];

    // The grants as the editor sends them back: as read, without the constraints of a grant that has none.
    private static JsonArray Sent(JsonArray read) =>
        [.. read.Select(grant => (JsonNode)(grant!["constraints"] is null
            ? new JsonObject { ["permission"] = grant["permission"]!.DeepClone() }
            : new JsonObject { ["permission"] = grant["permission"]!.DeepClone(), ["constraints"] = grant["constraints"]!.DeepClone() }))];

    private static HttpResponseMessage Answer(HttpStatusCode status, string code, string detail) =>
        new(status)
        {
            Content = new StringContent(
                new JsonObject { ["status"] = (int)status, ["code"] = code, ["title"] = "Forbidden", ["detail"] = detail }.ToJsonString(),
                Encoding.UTF8,
                "application/problem+json"),
        };
}
