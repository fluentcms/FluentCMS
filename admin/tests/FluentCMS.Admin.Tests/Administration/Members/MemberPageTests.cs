using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using Bunit.TestDoubles;
using FluentCMS.Admin.Administration.Members;
using FluentCMS.Admin.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Members;

// A member's page: its account and the actions on it, its groups, sessions and external logins, and each refusal by its
// meaning.
public sealed class MemberPageTests : MembersTestContext
{
    private static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    public MemberPageTests()
    {
        UseNeutralResources();
        GrantArea("members.manage", instanceOnly: true);
    }

    private string When(string moment) => Services.GetRequiredService<FormatService>().DateTime(DateTimeOffset.Parse(moment, System.Globalization.CultureInfo.InvariantCulture));

    private IRenderedComponent<AdminApp> Open(Guid member)
    {
        var app = Load($"members/{member:D}");
        app.WaitForAssertion(() =>
        {
            Section(app, "Sessions").QuerySelector("[role=status]").ShouldBeNull();
            Section(app, "External logins").QuerySelector("[role=status]").ShouldBeNull();
            Section(app, "Groups").QuerySelector("select").ShouldNotBeNull();
        });
        return app;
    }

    private static string Account(IRenderedComponent<AdminApp> app) => Section(app, "Account").QuerySelector("dl")!.TextContent;

    private static string[] Options(IRenderedComponent<AdminApp> app) =>
        [.. Section(app, "Groups").QuerySelectorAll("option").Select(option => option.TextContent)];

    private static string NameInput(IRenderedComponent<AdminApp> app) => Section(app, "Account").QuerySelector("input")!.GetAttribute("value") ?? "";

    private static void ConfirmDelete(IRenderedComponent<AdminApp> app)
    {
        Button(Section(app, "Account"), "Delete").Click();
        app.WaitForAssertion(() => Button(Section(app, "Account"), "Delete the member"));
        Button(Section(app, "Account"), "Delete the member").Click();
    }

    [Fact]
    public void TheMemberShowsItsAccountItsGroupsItsSessionsAndItsLogins()
    {
        MemberOf(Ada)["lockoutEnd"] = "2099-01-01T00:00:00+00:00";
        MemberOf(Ada)["lastLoginAt"] = "2026-11-19T09:00:00+00:00";

        var app = Open(Ada);

        Heading(app).ShouldBe("Ada");
        app.Find("main .cms-breadcrumb [aria-current=page]").TextContent.ShouldBe("Ada");
        var account = Account(app);
        account.ShouldContain("ada@example.com");
        account.ShouldContain("Verified");
        account.ShouldContain(When("2026-11-19T09:00:00+00:00"));
        account.ShouldContain($"Locked out until {When("2099-01-01T00:00:00+00:00")}. A lockout ends by itself; no action ends it early.");
        Section(app, "Account").QuerySelector("input")!.GetAttribute("value").ShouldBe("Ada");
        Buttons(Section(app, "Account")).ShouldBe(["Save the name", "Deactivate", "Delete"]);
        Items(Section(app, "Groups")).ShouldBe(["readers"]);
        Options(app).ShouldBe(["Choose a group", "writers"]);
        Items(Section(app, "Sessions")).ShouldBe([$"Shop, signed in {When(Created)}"]);
        Items(Section(app, "External logins")).ShouldBe(["GitHub"]);
    }

    [Fact]
    public void AMemberWithoutADisplayNameIsNamedByItsAddress()
    {
        var app = Open(Grace);

        Heading(app).ShouldBe("grace@example.com");
        Account(app).ShouldContain("Not verified");
        Section(app, "Groups").TextContent.ShouldContain("The member is in no group.");
    }

    [Fact]
    public void AMemberIsPutInAGroupAndTakenOutAgain()
    {
        var app = Open(Grace);

        Section(app, "Groups").QuerySelector("select")!.Change(Writers.ToString("D"));
        Button(Section(app, "Groups"), "Put in the group").Click();

        app.WaitForAssertion(() => Items(Section(app, "Groups")).ShouldBe(["writers"]));
        Message(app, "Groups").ShouldBe("The member was put in the group writers.");
        Options(app).ShouldBe(["Choose a group", "readers"]);
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Put && call.Path == $"members/{Grace:D}/groups/{Writers:D}");

        Button(Section(app, "Groups"), "Take out of the group writers").Click();

        app.WaitForAssertion(() => Message(app, "Groups").ShouldBe("The member was taken out of the group writers."));
        Section(app, "Groups").TextContent.ShouldContain("The member is in no group.");
        app.WaitForAssertion(() => ShouldBeFocused<MemberPage>(app, "groupsElement"));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == $"members/{Grace:D}/groups/{Writers:D}");
    }

    // Putting a member in a group, or taking it out, needs every grant of the group's role; the page keeps what stands.
    [Fact]
    public void ACallerBelowTheGroupsRoleSeesTheRefusalNamedAndTheGroupsAsTheyStand()
    {
        Management.Answer = (method, path) => path.Contains("/groups/", StringComparison.Ordinal) ? Problem(HttpStatusCode.Forbidden, "FCMS3103") : null;
        var app = Open(Ada);

        Section(app, "Groups").QuerySelector("select")!.Change(Writers.ToString("D"));
        Button(Section(app, "Groups"), "Put in the group").Click();

        app.WaitForAssertion(() => Message(app, "Groups").ShouldBe(
            "Not changed: the group's role gives a permission you do not hold. Putting a member in a group, or taking it out, needs every grant of the group's role."));
        Items(Section(app, "Groups")).ShouldBe(["readers"]);

        Button(Section(app, "Groups"), "Take out of the group readers").Click();

        app.WaitForAssertion(() => Management.Calls.Count(call => call.Method == HttpMethod.Delete).ShouldBe(1));
        app.WaitForAssertion(() => Message(app, "Groups").ShouldStartWith("Not changed: the group's role gives a permission you do not hold."));
        Items(Section(app, "Groups")).ShouldBe(["readers"]);
    }

    [Fact]
    public void AGroupChangedMeanwhileIsReadAgain()
    {
        var app = Open(Grace);
        Section(app, "Groups").QuerySelector("select")!.Change(Readers.ToString("D"));
        MemberOf(Grace)["groups"]!.AsArray().Add(new JsonObject { ["id"] = Readers, ["name"] = "readers" });
        Management.Answer = (method, path) => method == HttpMethod.Put ? Problem(HttpStatusCode.Conflict, "FCMS3171", "groupId") : null;

        Button(Section(app, "Groups"), "Put in the group").Click();

        app.WaitForAssertion(() => Message(app, "Groups").ShouldBe("The groups of the member changed meanwhile; they are shown as they now stand."));
        app.WaitForAssertion(() => Items(Section(app, "Groups")).ShouldBe(["readers"]));
    }

    // Deactivate and the removal of a login end the member's sessions on the server, so the card reads them again after
    // each and lists none of those ended; reactivate ends none.
    [Fact]
    public void DeactivateAsksFirstEndsTheSessionsAndTheMemberIsReactivatedAgain()
    {
        var app = Open(Ada);
        var sessions = $"members/{Ada:D}/sessions";

        Button(Section(app, "Account"), "Deactivate").Click();

        app.WaitForAssertion(() => Section(app, "Account").TextContent.ShouldContain("Deactivate Ada (ada@example.com)? The member's sessions end"));
        app.WaitForAssertion(() => ShouldBeFocused<MemberPage>(app, "questionElement"));
        Button(Section(app, "Account"), "Cancel").Click();
        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("Nothing was changed."));
        Management.Calls.ShouldNotContain(call => call.Path.EndsWith("/deactivate", StringComparison.Ordinal));

        Button(Section(app, "Account"), "Deactivate").Click();
        app.WaitForAssertion(() => Button(Section(app, "Account"), "Deactivate the member"));
        Button(Section(app, "Account"), "Deactivate the member").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The member was deactivated."));
        Account(app).ShouldContain($"Deactivated on {When("2026-11-20T10:00:00+00:00")}");
        app.WaitForAssertion(() => Section(app, "Sessions").TextContent.ShouldContain("The member is signed in nowhere."));
        Management.Count(sessions).ShouldBe(2);

        Sessions[Ada].Add(new JsonObject { ["id"] = Guid.NewGuid(), ["clientId"] = "app", ["clientName"] = "App", ["createdAt"] = Created, ["scopes"] = new JsonArray() });
        Button(Section(app, "Account"), "Reactivate").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The member was reactivated."));
        Account(app).ShouldContain("Active");
        Management.Count(sessions).ShouldBe(2);

        Button(Section(app, "External logins"), "Remove the login through GitHub").Click();

        app.WaitForAssertion(() => Management.Count(sessions).ShouldBe(3));
        app.WaitForAssertion(() => Section(app, "Sessions").TextContent.ShouldContain("The member is signed in nowhere."));
        Message(app, "External logins").ShouldBe("The login through GitHub was removed.");
    }

    [Fact]
    public void DeleteAsksFirstNamingTheMemberAndGoesBackToTheList()
    {
        var app = Open(Ada);
        var announced = new List<string>();
        Services.GetRequiredService<FluentCMS.Admin.Accessibility.Announcer>().Announced += (message, _) => announced.Add(message);

        Button(Section(app, "Account"), "Delete").Click();

        app.WaitForAssertion(() => Section(app, "Account").TextContent.ShouldContain(
            "Delete Ada (ada@example.com)? The member is removed with its groups and logins, and its sessions end. This cannot be undone."));
        app.WaitForAssertion(() => ShouldBeFocused<MemberPage>(app, "questionElement"));
        Buttons(Section(app, "Account")).ShouldBe(["Save the name", "Delete the member", "Cancel"]);
        Button(Section(app, "Account"), "Cancel").Click();
        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("Nothing was changed."));
        Management.Calls.ShouldNotContain(call => call.Method == HttpMethod.Delete);

        Button(Section(app, "Account"), "Delete").Click();
        app.WaitForAssertion(() => Button(Section(app, "Account"), "Delete the member"));
        Button(Section(app, "Account"), "Delete the member").Click();

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith("/members"));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == $"members/{Ada:D}");
        announced.ShouldContain("The member Ada (ada@example.com) was deleted.");
        app.WaitForAssertion(() => app.FindAll("main table tbody tr").Select(row => row.QuerySelector("td")!.TextContent.Trim()).ShouldBe(["grace@example.com"]));
    }

    // The list it goes back to takes the place of the member's entry of the history, so that Back does not reach the address
    // of a member that is gone.
    [Fact]
    public void ADeleteGoesBackToTheListAsItWasOpenedInPlaceOfTheMember()
    {
        var list = $"members?group={Readers:D}&active=true";
        var app = Load(list);
        app.WaitForAssertion(() => app.FindAll("main table tbody tr").Count.ShouldBe(1));
        app.Find("main table tbody tr").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        app.WaitForAssertion(() => Section(app, "Groups").QuerySelector("select").ShouldNotBeNull());
        Navigation.Uri.ShouldEndWith($"/members/{Ada:D}");

        ConfirmDelete(app);

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith($"/{list}"));
        app.WaitForAssertion(() => app.Find("main table").TextContent.ShouldContain("Nothing matches the search and filters."));
        var history = Services.GetRequiredService<BunitNavigationManager>().History;
        history.First().Uri.ShouldBe(list);
        history.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
        history.ShouldNotContain(entry => entry.Uri == $"members/{Ada:D}");
    }

    // The history entry may hold anything a script of the page's origin put there, so an address that is not the members
    // list's goes back to the plain list.
    [Fact]
    public void AForeignAddressInTheHistoryEntryGoesBackToThePlainList()
    {
        var app = Load("members");
        app.WaitForAssertion(() => app.FindAll("main table tbody tr").Count.ShouldBe(2));
        Navigation.NavigateTo($"members/{Ada:D}", new NavigationOptions { HistoryEntryState = "https://evil.example/" });
        app.WaitForAssertion(() => Section(app, "Groups").QuerySelector("select").ShouldNotBeNull());

        ConfirmDelete(app);

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe("http://localhost/members"));
        app.WaitForAssertion(() => app.FindAll("main table tbody tr").Select(row => row.QuerySelector("td")!.TextContent.Trim()).ShouldBe(["grace@example.com"]));
    }

    [Fact]
    public void ADeleteOfAMemberChangedMeanwhileSaysSoAndReadsItAgain()
    {
        Management.Answer = (method, _) => method == HttpMethod.Delete ? Problem(HttpStatusCode.Conflict, "FCMS3174") : null;
        var app = Open(Ada);
        MemberOf(Ada)["displayName"] = "Ada L.";

        ConfirmDelete(app);

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The member changed meanwhile; it is shown as it now stands."));
        app.WaitForAssertion(() => Heading(app).ShouldBe("Ada L."));
        NameInput(app).ShouldBe("Ada L.");
    }

    // An input left as it was follows the server, so a name another administrator saved meanwhile is not sent back over.
    [Fact]
    public void AnUntouchedNameTakesTheNameSavedMeanwhileAndASaveSendsIt()
    {
        var app = Open(Ada);
        MemberOf(Ada)["displayName"] = "Ada L.";

        Button(Section(app, "Account"), "Deactivate").Click();
        app.WaitForAssertion(() => Button(Section(app, "Account"), "Deactivate the member"));
        Button(Section(app, "Account"), "Deactivate the member").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The member was deactivated."));
        Heading(app).ShouldBe("Ada L.");
        NameInput(app).ShouldBe("Ada L.");

        Section(app, "Account").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The display name was saved."));
        JsonNode.Parse(BodyOf(HttpMethod.Patch, $"members/{Ada:D}")!)!.ToJsonString().ShouldBe("""{"displayName":"Ada L."}""");
    }

    // Only the save of the name puts the server's name in the input; another answer keeps what was typed and not saved.
    [Fact]
    public void ANameTypedAndNotSavedIsKeptWhenAnotherChangeAnswers()
    {
        var app = Open(Ada);
        Section(app, "Account").QuerySelector("input")!.Input("Ada typed");

        Section(app, "Groups").QuerySelector("select")!.Change(Writers.ToString("D"));
        Button(Section(app, "Groups"), "Put in the group").Click();
        app.WaitForAssertion(() => Message(app, "Groups").ShouldBe("The member was put in the group writers."));
        Button(Section(app, "Account"), "Deactivate").Click();
        app.WaitForAssertion(() => Button(Section(app, "Account"), "Deactivate the member"));
        Button(Section(app, "Account"), "Deactivate the member").Click();
        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The member was deactivated."));
        Button(Section(app, "Account"), "Reactivate").Click();
        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The member was reactivated."));

        Section(app, "Account").QuerySelector("input")!.GetAttribute("value").ShouldBe("Ada typed");
        Heading(app).ShouldBe("Ada");

        Section(app, "Account").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Heading(app).ShouldBe("Ada typed"));
        Section(app, "Account").QuerySelector("input")!.GetAttribute("value").ShouldBe("Ada typed");
    }

    [Fact]
    public void ADeleteThatFailsKeepsTheMemberAndSaysSo()
    {
        Management.Answer = (method, _) => method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;
        var app = Open(Ada);

        Button(Section(app, "Account"), "Delete").Click();
        app.WaitForAssertion(() => Button(Section(app, "Account"), "Delete the member"));
        Button(Section(app, "Account"), "Delete the member").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The member could not be deleted. Try again."));
        Heading(app).ShouldBe("Ada");
        Buttons(Section(app, "Account")).ShouldBe(["Save the name", "Deactivate", "Delete"]);
    }

    [Fact]
    public void TheDisplayNameIsChangedClearedAndAnInvalidOneIsShownOnTheField()
    {
        var app = Open(Ada);
        var name = Section(app, "Account").QuerySelector("input")!;

        // The input takes the name the save answered, not the text as typed.
        name.Input("  Ada L.  ");
        Section(app, "Account").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The display name was saved."));
        JsonNode.Parse(BodyOf(HttpMethod.Patch, $"members/{Ada:D}")!)!.ToJsonString().ShouldBe("""{"displayName":"Ada L."}""");
        Heading(app).ShouldBe("Ada L.");
        NameInput(app).ShouldBe("Ada L.");

        Section(app, "Account").QuerySelector("input")!.Input("");
        Section(app, "Account").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The display name was cleared."));
        JsonNode.Parse(BodyOf(HttpMethod.Patch, $"members/{Ada:D}")!)!.ToJsonString().ShouldBe("""{"displayName":""}""");
        Heading(app).ShouldBe("ada@example.com");

        Management.Answer = (method, _) => method == HttpMethod.Patch ? Problem(HttpStatusCode.UnprocessableEntity, "FCMS3162", "displayName") : null;
        Section(app, "Account").QuerySelector("input")!.Input("Bad");
        Section(app, "Account").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Section(app, "Account").TextContent.ShouldContain("A display name has at most 128 characters and no control character."));
        Heading(app).ShouldBe("ada@example.com");
    }

    [Fact]
    public void ASessionIsEndedAndALastWayToSignInIsKept()
    {
        var app = Open(Ada);

        Button(Section(app, "Sessions"), $"End the session of Shop signed in {When(Created)}").Click();

        app.WaitForAssertion(() => Message(app, "Sessions").ShouldBe("The session of Shop was ended."));
        Section(app, "Sessions").TextContent.ShouldContain("The member is signed in nowhere.");
        app.WaitForAssertion(() => ShouldBeFocused<MemberSessions>(app, "messageElement"));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == $"members/{Ada:D}/sessions/{Laptop:D}");

        Management.Answer = (method, path) => method == HttpMethod.Delete && path.Contains("/logins/", StringComparison.Ordinal) ? Problem(HttpStatusCode.Conflict, "FCMS3184", "provider") : null;
        Button(Section(app, "External logins"), "Remove the login through GitHub").Click();

        app.WaitForAssertion(() => Message(app, "External logins").ShouldBe("Not removed: the login through GitHub is the member's last way to sign in."));
        Items(Section(app, "External logins")).ShouldBe(["GitHub"]);
    }

    [Fact]
    public void AnUnknownMemberIsNotFoundWithAWayBackToTheList()
    {
        var app = Load($"members/{Guid.NewGuid():D}");

        app.WaitForAssertion(() => Heading(app).ShouldBe("Member not found"));
        app.Find("main p a[href='members']").TextContent.ShouldBe("Back to the members");
    }

    [Fact]
    public void ACallerWhoHoldsMembersManageInASiteOnlyGetsNoMemberPage()
    {
        GrantArea("members.manage", instanceOnly: false);

        var app = Load($"members/{Ada:D}");

        app.WaitForAssertion(() => Heading(app).ShouldBe(Text("Shell.NoAccess.Heading")));
        Management.Calls.ShouldNotContain(call => call.Path.StartsWith("member", StringComparison.Ordinal));
    }

    // #3511: a display name typed and not saved asks before the page is left, and nothing of it is kept as a draft.
    [Fact]
    public async Task ADisplayNameNotSavedAsksBeforeThePageIsLeftAndKeepsNoDraft()
    {
        Management.AddSite(Shop, "Shop", "shop");
        var app = Open(Ada);
        Section(app, "Account").QuerySelector("input")!.Input("Ada Lovelace");

        await LeavingAsksFirstAsync(app, () => NameInput(app).ShouldBe("Ada Lovelace"), "Ada Lovelace");

        LeaveWhenAsked(app, "members");
    }

    [Fact]
    public void ADisplayNameChangedBackOrSavedAsksNothing()
    {
        var app = Open(Ada);
        Section(app, "Account").QuerySelector("input")!.Input("Ada Lovelace");
        Section(app, "Account").QuerySelector("input")!.Input("Ada ");
        Services.GetRequiredService<FluentCMS.Admin.Forms.IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();

        Section(app, "Account").QuerySelector("input")!.Input("Ada Lovelace");
        Section(app, "Account").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The display name was saved."));
        NothingAsks(app, "members");
    }
}
