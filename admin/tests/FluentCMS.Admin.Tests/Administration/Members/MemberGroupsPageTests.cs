using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Members;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Members;

// The member groups page: the groups with their role, one made, changed and deleted after a question that names it, and
// each refusal by its meaning.
public sealed class MemberGroupsPageTests : MembersTestContext
{
    public MemberGroupsPageTests()
    {
        UseNeutralResources();
        GrantArea("members.manage", instanceOnly: true);
    }

    private IRenderedComponent<AdminApp> Open()
    {
        var app = Load("members/groups");
        app.WaitForAssertion(() =>
        {
            Items(Section(app, "Groups")).Length.ShouldBe(Groups.Count);
            Form(app).QuerySelector("select").ShouldNotBeNull();
        });
        return app;
    }

    // The card of the form, whose heading names what it does.
    private static AngleSharp.Dom.IElement Form(IRenderedComponent<AdminApp> app) =>
        app.FindAll("section.cms-card").Single(section => section.QuerySelector("form") is not null);

    private static void Fill(IRenderedComponent<AdminApp> app, string name, string? description, Guid role)
    {
        var inputs = Form(app).QuerySelectorAll("input");
        inputs[0].Input(name);
        inputs[1].Input(description ?? "");
        Form(app).QuerySelector("select")!.Change(role.ToString("D"));
    }

    [Fact]
    public void EachGroupShowsItsRoleAndItsMembersWhichLinkToTheFilteredList()
    {
        GroupOf(Readers)["description"] = "People who read";

        var app = Open();

        Heading(app).ShouldBe("Member groups");
        app.Find("main .cms-breadcrumb [aria-current=page]").TextContent.ShouldBe("Member groups");
        Items(Section(app, "Groups")).ShouldBe(
        [
            "readers Role: Reader People who read 1 member in the group readers",
            "writers Role: Author (Main) 0 members in the group writers",
        ]);
        Section(app, "Groups").QuerySelector("a")!.GetAttribute("href").ShouldBe($"members?group={Readers:D}");
        Buttons(Section(app, "Groups")).ShouldBe(["Change the group readers", "Delete the group readers", "Change the group writers", "Delete the group writers"]);
    }

    [Fact]
    public void AGroupIsCreatedBoundToARole()
    {
        var app = Open();
        Form(app).QuerySelector("h2")!.TextContent.ShouldBe("New group");

        Fill(app, "vip", "Paying members", Author);
        Form(app).QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "New group").ShouldBe("The group vip was created."));
        JsonNode.Parse(BodyOf(HttpMethod.Post, "member-groups")!)!.ToJsonString()
            .ShouldBe($$"""{"name":"vip","description":"Paying members","roleId":"{{Author:D}}"}""");
        app.WaitForAssertion(() => Items(Section(app, "Groups")).ShouldContain("vip Role: Author (Main) Paying members 0 members in the group vip"));
        Form(app).QuerySelectorAll("input")[0].GetAttribute("value").ShouldBeNullOrEmpty();
        app.WaitForAssertion(() => ShouldBeFocused<MemberGroupsPage>(app, "formElement"));
    }

    [Fact]
    public void AGroupWithoutANameOrARoleIsNotSentAndTheServersRefusalsShowOnTheirFields()
    {
        var app = Open();

        Form(app).QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Form(app).TextContent.ShouldContain("Enter the name of the group."));
        Form(app).TextContent.ShouldContain("Choose the role the members of the group hold.");
        Management.Calls.ShouldNotContain(call => call.Method == HttpMethod.Post);

        Management.Answer = (method, _) => method == HttpMethod.Post ? Problem(HttpStatusCode.Conflict, "FCMS3170", "name") : null;
        Fill(app, "readers", null, Reader);
        Form(app).QuerySelector("form")!.Submit();
        app.WaitForAssertion(() => Form(app).TextContent.ShouldContain("A group has this name already."));

        Management.Answer = (method, _) => method == HttpMethod.Post ? Problem(HttpStatusCode.UnprocessableEntity, "FCMS3169", "name") : null;
        Fill(app, "Bad Name", null, Reader);
        Form(app).QuerySelector("form")!.Submit();
        app.WaitForAssertion(() => Form(app).TextContent.ShouldContain("A group name is 1 to 64 lower-case letters"));
        Items(Section(app, "Groups")).Length.ShouldBe(2);
    }

    [Fact]
    public void AGroupIsChangedItsNameAndItsRole()
    {
        var app = Open();

        Button(Section(app, "Groups"), "Change the group writers").Click();

        app.WaitForAssertion(() => Form(app).QuerySelector("h2")!.TextContent.ShouldBe("Change the group writers"));
        Form(app).QuerySelectorAll("input")[0].GetAttribute("value").ShouldBe("writers");
        Form(app).QuerySelector("select")!.GetAttribute("value").ShouldBe(Author.ToString("D"));
        Fill(app, "authors", null, Reader);
        Buttons(Form(app)).ShouldBe(["Save the group", "Cancel"]);
        Form(app).QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "New group").ShouldBe("The group authors was saved."));
        JsonNode.Parse(BodyOf(HttpMethod.Put, $"member-groups/{Writers:D}")!)!.ToJsonString()
            .ShouldBe($$"""{"name":"authors","description":null,"roleId":"{{Reader:D}}"}""");
        app.WaitForAssertion(() => Items(Section(app, "Groups")).ShouldContain("authors Role: Reader 0 members in the group authors"));
    }

    [Fact]
    public void AChangeIsCancelledAndTheFormIsNewAgain()
    {
        var app = Open();
        Button(Section(app, "Groups"), "Change the group writers").Click();
        app.WaitForAssertion(() => Button(Form(app), "Cancel"));

        Button(Form(app), "Cancel").Click();

        app.WaitForAssertion(() => Message(app, "New group").ShouldBe("Nothing was changed."));
        Form(app).QuerySelectorAll("input")[0].GetAttribute("value").ShouldBeNullOrEmpty();
        Management.Calls.ShouldNotContain(call => call.Method == HttpMethod.Put);
    }

    [Fact]
    public void DeleteAsksFirstNamingTheGroup()
    {
        var app = Open();

        Button(Section(app, "Groups"), "Delete the group readers").Click();

        app.WaitForAssertion(() => Section(app, "Groups").TextContent.ShouldContain("Delete the group readers? Its members leave it and lose what its role gives them."));
        app.WaitForAssertion(() => ShouldBeFocused<MemberGroupsPage>(app, "questionElement"));
        Button(Section(app, "Groups"), "Cancel").Click();
        app.WaitForAssertion(() => Message(app, "Groups").ShouldBe("Nothing was changed."));
        Management.Calls.ShouldNotContain(call => call.Method == HttpMethod.Delete);

        Button(Section(app, "Groups"), "Delete the group readers").Click();
        app.WaitForAssertion(() => Button(Section(app, "Groups"), "Delete the group"));
        Button(Section(app, "Groups"), "Delete the group").Click();

        app.WaitForAssertion(() => Message(app, "Groups").ShouldBe("The group readers was deleted."));
        Items(Section(app, "Groups")).ShouldBe(["writers Role: Author (Main) 0 members in the group writers"]);
        app.WaitForAssertion(() => ShouldBeFocused<MemberGroupsPage>(app, "listElement"));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == $"member-groups/{Readers:D}");
    }

    // Making, changing or deleting a group needs every grant of its role (FCMS3103); nothing shown changes.
    [Fact]
    public void ACallerBelowTheGroupsRoleSeesTheRefusalNamed()
    {
        Management.Answer = (method, _) => method == HttpMethod.Get ? null : Problem(HttpStatusCode.Forbidden, "FCMS3103");
        var app = Open();

        Fill(app, "vip", null, Author);
        Form(app).QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "New group").ShouldBe(
            "Not saved: the role gives a permission you do not hold. Making or changing a group needs every grant of its role, the one it has and the one it gets."));
        Form(app).QuerySelectorAll("input")[0].GetAttribute("value").ShouldBe("vip");
        Items(Section(app, "Groups")).Length.ShouldBe(2);

        Button(Section(app, "Groups"), "Delete the group writers").Click();
        app.WaitForAssertion(() => Button(Section(app, "Groups"), "Delete the group"));
        Button(Section(app, "Groups"), "Delete the group").Click();

        app.WaitForAssertion(() => Message(app, "Groups").ShouldStartWith("The group writers was not deleted: its role gives a permission you do not hold."));
        Items(Section(app, "Groups")).Length.ShouldBe(2);
        Buttons(Section(app, "Groups")).ShouldContain("Delete the group writers");
    }
}
