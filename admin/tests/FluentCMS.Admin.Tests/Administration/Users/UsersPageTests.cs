using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Users;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Plugins;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Users;

// The users page: the list, read in pages with a search, and the invitations, sent with roles, pending and revoked.
public sealed class UsersPageTests : UsersTestContext
{
    private static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    public UsersPageTests()
    {
        UseNeutralResources();
        GrantArea("users.manage", instanceOnly: true);
    }

    private string When(string moment) => Services.GetRequiredService<FormatService>().DateTime(DateTimeOffset.Parse(moment, System.Globalization.CultureInfo.InvariantCulture));

    private static string[][] Rows(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("main table tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToArray())];

    [Fact]
    public void EachUserShowsItsRolesAndItsStateAndASearchAsksTheServer()
    {
        var grace = UserOf(Grace);
        grace["lockoutEnd"] = "2099-01-01T00:00:00+00:00";
        grace["mustChangePassword"] = true;
        UserOf(Ada)["deactivatedAt"] = Created;

        var app = Load("users");

        app.WaitForAssertion(() => Rows(app).ShouldBe(
        [
            ["ada", "ada@example.com", "Super Admin", "Deactivated, Two-factor sign-in on"],
            ["grace", "grace@example.com", "Editor, Author (Main)", $"Active, Locked out until {When("2099-01-01T00:00:00+00:00")}, Must change the password"],
        ]));
        UserQueries.ShouldBe(["page=1&pageSize=25"]);

        app.Find("main form[role=search] input").Input("gra");
        app.Find("main form[role=search]").Submit();

        app.WaitForAssertion(() => Rows(app).Select(row => row[0]).ShouldBe(["grace"]));
        UserQueries[^1].ShouldBe("page=1&pageSize=25&search=gra");
        Navigation.Uri.ShouldEndWith("/users?q=gra");
    }

    [Fact]
    public void EnterOnARowOpensTheUserAndItsNameLinksToIt()
    {
        var app = Load("users");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));

        var row = app.FindAll("main table tbody tr")[1];
        row.QuerySelector("a")!.GetAttribute("href").ShouldBe($"users/{Grace:D}");
        row.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith($"/users/{Grace:D}"));
        app.WaitForAssertion(() => Heading(app).ShouldBe("grace"));
    }

    [Fact]
    public void AnInvitationIsSentWithItsRolesShowsAsPendingAndIsRevoked()
    {
        var app = Load("users");
        app.WaitForAssertion(() => Section(app, "Invite a user").QuerySelector("select").ShouldNotBeNull());
        Section(app, "Pending invitations").TextContent.ShouldContain("No invitation is waiting to be accepted.");

        Section(app, "Invite a user").QuerySelector("input[type=email]")!.Input("lin@example.com");
        Section(app, "Invite a user").QuerySelector("select")!.Change(Editor.ToString("D"));
        Button(Section(app, "Invite a user"), "Add the role").Click();
        app.WaitForAssertion(() => Items(Section(app, "Invite a user")).ShouldBe(["Editor"]));
        Section(app, "Invite a user").QuerySelector("select")!.Change(Author.ToString("D"));
        Button(Section(app, "Invite a user"), "Add the role").Click();
        app.WaitForAssertion(() => Items(Section(app, "Invite a user")).ShouldBe(["Editor", "Author (Main)"]));

        // A role chosen is offered no more.
        Section(app, "Invite a user").QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["Choose a role", "Super Admin"]);
        Section(app, "Invite a user").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "Invite a user").ShouldBe($"An invitation was sent to lin@example.com. It can be accepted until {When("2026-11-09T08:00:00+00:00")}."));
        var sent = JsonNode.Parse(BodyOf(HttpMethod.Post, "invitations")!)!;
        sent["email"]!.GetValue<string>().ShouldBe("lin@example.com");
        sent["roleIds"]!.AsArray().Select(id => id!.GetValue<string>()).ShouldBe([Editor.ToString("D"), Author.ToString("D")]);
        app.WaitForAssertion(() => Items(Section(app, "Pending invitations")).ShouldBe(
            [$"lin@example.com Roles: Editor, Author (Main) Sent {When(Created)}, open until {When("2026-11-09T08:00:00+00:00")}"]));
        Items(Section(app, "Invite a user")).ShouldBeEmpty();
        Section(app, "Invite a user").QuerySelector("input[type=email]")!.GetAttribute("value").ShouldBeNullOrEmpty();

        Button(Section(app, "Pending invitations"), "Revoke the invitation to lin@example.com").Click();

        app.WaitForAssertion(() => Message(app, "Pending invitations").ShouldBe("The invitation to lin@example.com was revoked."));
        Section(app, "Pending invitations").TextContent.ShouldContain("No invitation is waiting to be accepted.");
        app.WaitForAssertion(() => ShouldBeFocused<UserInvitations>(app, "listElement"));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path.StartsWith("invitations/", StringComparison.Ordinal));
    }

    [Fact]
    public void ARoleChosenByMistakeIsTakenOutOfTheInvitation()
    {
        var app = Load("users");
        app.WaitForAssertion(() => Section(app, "Invite a user").QuerySelector("select").ShouldNotBeNull());
        Section(app, "Invite a user").QuerySelector("select")!.Change(Editor.ToString("D"));
        Button(Section(app, "Invite a user"), "Add the role").Click();
        app.WaitForAssertion(() => Items(Section(app, "Invite a user")).ShouldBe(["Editor"]));

        Button(Section(app, "Invite a user"), "Remove the role Editor from the invitation").Click();

        app.WaitForAssertion(() => Items(Section(app, "Invite a user")).ShouldBeEmpty());
        Section(app, "Invite a user").TextContent.ShouldContain("No role chosen");
    }

    [Fact]
    public void AnInvitationWithoutAnAddressIsNotSentAndOneTheServerRefusesShowsWhy()
    {
        var app = Load("users");
        app.WaitForAssertion(() => Section(app, "Invite a user").QuerySelector("select").ShouldNotBeNull());

        Section(app, "Invite a user").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Section(app, "Invite a user").TextContent.ShouldContain("Enter the email address to send the invitation to."));
        Management.Calls.ShouldNotContain(call => call.Method == HttpMethod.Post);

        Management.Answer = (method, path) => method == HttpMethod.Post && path == "invitations" ? Problem(HttpStatusCode.Conflict, "FCMS3003", "email") : null;
        Section(app, "Invite a user").QuerySelector("input[type=email]")!.Input("grace@example.com");
        Section(app, "Invite a user").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Section(app, "Invite a user").TextContent.ShouldContain("A user has this email address already."));
    }

    // Sending or revoking an invitation needs every grant of its roles, and of the roles of a pending one it would end.
    [Fact]
    public void AnInvitationAboveTheCallersReachIsRefusedByWhatItMeans()
    {
        Invitations.Add(new JsonObject
        {
            ["id"] = Laptop,
            ["email"] = "sam@example.com",
            ["roleIds"] = new JsonArray(SuperAdmin),
            ["createdAt"] = Created,
            ["createdBy"] = Ada,
            ["expiresAt"] = "2026-11-09T08:00:00+00:00",
        });
        Management.Answer = (method, path) => method == HttpMethod.Post || method == HttpMethod.Delete ? Problem(HttpStatusCode.Forbidden, "FCMS3103") : null;
        var app = Load("users");
        app.WaitForAssertion(() => Items(Section(app, "Pending invitations")).ShouldHaveSingleItem().ShouldStartWith("sam@example.com Roles: Super Admin"));

        Section(app, "Invite a user").QuerySelector("input[type=email]")!.Input("sam@example.com");
        Section(app, "Invite a user").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "Invite a user").ShouldStartWith("Not sent: a role of the invitation, or of a pending invitation to this address, gives a permission you do not hold."));

        Button(Section(app, "Pending invitations"), "Revoke the invitation to sam@example.com").Click();

        app.WaitForAssertion(() => Message(app, "Pending invitations").ShouldStartWith("The invitation to sam@example.com was not revoked: one of its roles gives a permission you do not hold."));
        Items(Section(app, "Pending invitations")).ShouldHaveSingleItem();
    }

    // A send ends the pending invitation to the address before the mail goes, and a mail that fails does not bring it back.
    [Fact]
    public void AFailedSendReadsThePendingInvitationsAgain()
    {
        Invitations.Add(new JsonObject
        {
            ["id"] = Laptop,
            ["email"] = "sam@example.com",
            ["roleIds"] = new JsonArray(),
            ["createdAt"] = Created,
            ["createdBy"] = Ada,
            ["expiresAt"] = "2026-11-09T08:00:00+00:00",
        });
        Management.Answer = (method, path) =>
        {
            if (method != HttpMethod.Post || path != "invitations")
            {
                return null;
            }

            Invitations.Clear();
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        };
        var app = Load("users");
        app.WaitForAssertion(() => Items(Section(app, "Pending invitations")).ShouldHaveSingleItem());

        Section(app, "Invite a user").QuerySelector("input[type=email]")!.Input("sam@example.com");
        Section(app, "Invite a user").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "Invite a user").ShouldBe("The invitation could not be sent. Try again."));
        app.WaitForAssertion(() => Section(app, "Pending invitations").TextContent.ShouldContain("No invitation is waiting to be accepted."));
        Management.Count("invitations").ShouldBe(3);
    }

    [Fact]
    public void AnInvitationNoLongerPendingIsSaidAndTheListReadAgain()
    {
        Invitations.Add(new JsonObject
        {
            ["id"] = Laptop,
            ["email"] = "sam@example.com",
            ["roleIds"] = new JsonArray(),
            ["createdAt"] = Created,
            ["createdBy"] = Ada,
            ["expiresAt"] = "2026-11-09T08:00:00+00:00",
        });
        var app = Load("users");
        app.WaitForAssertion(() => Items(Section(app, "Pending invitations")).ShouldHaveSingleItem().ShouldStartWith("sam@example.com No role"));
        Invitations.Clear();

        Button(Section(app, "Pending invitations"), "Revoke the invitation to sam@example.com").Click();

        app.WaitForAssertion(() => Message(app, "Pending invitations").ShouldBe("The invitation to sam@example.com is no longer pending: it was accepted, revoked or has expired."));
        Section(app, "Pending invitations").TextContent.ShouldContain("No invitation is waiting to be accepted.");
    }

    // The page draws the list again while the router waits for the first load of a half: the list reads nothing at the
    // half's address, and reads once the address is its own again, when that navigation overtakes the wait (#3514).
    [Fact]
    public async Task AListDrawnAgainWhileTheRouterWaitsReadsNothingThereAndReadsOnItsReturn()
    {
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([sample with { Widgets = [] }], []));
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
        Grant(["reviews.notes.manage"], "users.manage");
        var app = Load("users?q=gra");
        app.WaitForAssertion(() => Rows(app).Select(row => row[0]).ShouldBe(["grace"]));
        var reads = UserQueries.Count;
        var hold = Loader.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Navigation.NavigateTo("reviews");
        app.WaitForAssertion(() => Loader.Loads.ShouldBe(2));

        app.FindComponent<UsersPage>().Render();
        await app.InvokeAsync(() => { });

        UserQueries.Count.ShouldBe(reads);
        Loading(app).ShouldNotBeNull();
        Navigation.NavigateTo("users?q=ada");
        app.WaitForAssertion(() => Rows(app).Select(row => row[0]).ShouldBe(["ada"]));
        UserQueries[^1].ShouldBe("page=1&pageSize=25&search=ada");
        hold.SetResult();
    }

    // #3511: an invitation typed and not sent asks before the page is left, a search of the list keeps it, and nothing of it
    // is kept as a draft.
    [Fact]
    public async Task AnInvitationNotSentAsksBeforeThePageIsLeftAndKeepsNoDraft()
    {
        Management.AddSite(Shop, "Shop", "shop");
        var app = Load("users");
        app.WaitForAssertion(() => Section(app, "Invite a user").QuerySelector("select").ShouldNotBeNull());
        Section(app, "Invite a user").QuerySelector("input[type=email]")!.Input("lin@example.com");
        Section(app, "Invite a user").QuerySelector("select")!.Change(Editor.ToString("D"));
        Button(Section(app, "Invite a user"), "Add the role").Click();
        app.WaitForAssertion(() => Items(Section(app, "Invite a user")).ShouldBe(["Editor"]));

        Navigation.NavigateTo("users?q=gra");
        app.WaitForAssertion(() => Rows(app).Select(row => row[0]).ShouldBe(["grace"]));
        await LeavingAsksFirstAsync(app, () =>
        {
            Section(app, "Invite a user").QuerySelector("input[type=email]")!.GetAttribute("value").ShouldBe("lin@example.com");
            Items(Section(app, "Invite a user")).ShouldBe(["Editor"]);
        }, "lin@example.com");

        LeaveWhenAsked(app, "");
    }

    [Fact]
    public void AnInvitationClearedOrSentAsksNothing()
    {
        var app = Load("users");
        app.WaitForAssertion(() => Section(app, "Invite a user").QuerySelector("select").ShouldNotBeNull());
        Section(app, "Invite a user").QuerySelector("input[type=email]")!.Input("lin@example.com");
        Section(app, "Invite a user").QuerySelector("input[type=email]")!.Input("");
        Services.GetRequiredService<FluentCMS.Admin.Forms.IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();

        Section(app, "Invite a user").QuerySelector("input[type=email]")!.Input("lin@example.com");
        Section(app, "Invite a user").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "Invite a user").ShouldStartWith("An invitation was sent to lin@example.com."));
        NothingAsks(app);
    }
}
