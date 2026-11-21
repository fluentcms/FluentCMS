using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Members;

// The members page: the list, read in pages with a search and the filters group, verified and active, and a member added.
public sealed class MembersPageTests : MembersTestContext
{
    public MembersPageTests()
    {
        UseNeutralResources();
        GrantArea("members.manage", instanceOnly: true);
    }

    private static string[][] Rows(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("main table tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToArray())];

    private static AngleSharp.Dom.IElement Filter(IRenderedComponent<AdminApp> app, string label)
    {
        var field = app.FindAll("main [role=group] label").Single(item => item.TextContent.Trim() == label);
        return app.Find($"#{field.GetAttribute("for")}");
    }

    [Fact]
    public void EachMemberShowsItsGroupsAndItsStateAndASearchAsksTheServer()
    {
        MemberOf(Grace)["deactivatedAt"] = Created;

        var app = Load("members");

        app.WaitForAssertion(() => Rows(app).ShouldBe(
        [
            ["ada@example.com", "Ada", "readers", "Active, Address verified", "Never"],
            ["grace@example.com", "", "", "Deactivated, Address not verified", "Never"],
        ]));
        MemberQueries.ShouldBe(["page=1&pageSize=25"]);
        app.Find("main a[href='members/groups']").TextContent.ShouldBe("Member groups");

        app.Find("main form[role=search] input").Input("gra");
        app.Find("main form[role=search]").Submit();

        app.WaitForAssertion(() => Rows(app).Select(row => row[0]).ShouldBe(["grace@example.com"]));
        MemberQueries[^1].ShouldBe("page=1&pageSize=25&search=gra");
        Navigation.Uri.ShouldEndWith("/members?q=gra");
    }

    [Fact]
    public void TheGroupVerifiedAndActiveFiltersAreInTheAddressAndAskTheServer()
    {
        var app = Load("members");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
        Filter(app, "Group").QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["Any group", "readers", "writers"]);

        Filter(app, "Group").Change(Readers.ToString("D"));

        app.WaitForAssertion(() => Rows(app).Select(row => row[0]).ShouldBe(["ada@example.com"]));
        MemberQueries[^1].ShouldBe($"page=1&pageSize=25&group={Readers:D}");
        Navigation.Uri.ShouldEndWith($"/members?group={Readers:D}");
        app.WaitForAssertion(() => Filter(app, "Group").GetAttribute("value").ShouldBe(Readers.ToString("D")));

        Filter(app, "Group").Change("");
        Filter(app, "Email address").Change("false");

        app.WaitForAssertion(() => Rows(app).Select(row => row[0]).ShouldBe(["grace@example.com"]));
        MemberQueries[^1].ShouldBe("page=1&pageSize=25&verified=false");

        Filter(app, "State").Change("false");

        app.WaitForAssertion(() => Rows(app).ShouldBe([["Nothing matches the search and filters."]]));
        MemberQueries[^1].ShouldBe("page=1&pageSize=25&verified=false&active=false");
    }

    [Fact]
    public void AFilterInTheAddressIsReadOnTheFirstVisit()
    {
        var app = Load($"members?group={Readers:D}&active=true");

        app.WaitForAssertion(() => Rows(app).Select(row => row[0]).ShouldBe(["ada@example.com"]));
        MemberQueries.ShouldBe([$"page=1&pageSize=25&group={Readers:D}&active=true"]);
        app.WaitForAssertion(() => Filter(app, "Group").GetAttribute("value").ShouldBe(Readers.ToString("D")));
        Filter(app, "State").GetAttribute("value").ShouldBe("true");
    }

    [Fact]
    public void EnterOnARowOpensTheMemberAndItsAddressLinksToIt()
    {
        var app = Load("members");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));

        var row = app.FindAll("main table tbody tr")[0];
        row.QuerySelector("a")!.GetAttribute("href").ShouldBe($"members/{Ada:D}");
        row.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith($"/members/{Ada:D}"));
        app.WaitForAssertion(() => Heading(app).ShouldBe("Ada"));
    }

    [Fact]
    public void AMemberIsAddedAndItsPageOpens()
    {
        var app = Load("members");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
        var add = Section(app, "Add a member");

        add.QuerySelector("input[type=email]")!.Input("lin@example.com");
        add.QuerySelectorAll("input")[1].Input("Lin");
        add.QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Heading(app).ShouldBe("Lin"));
        var sent = JsonNode.Parse(BodyOf(HttpMethod.Post, "members")!)!;
        sent.ToJsonString().ShouldBe("""{"email":"lin@example.com","displayName":"Lin"}""");
        Navigation.Uri.ShouldEndWith($"/members/{(Guid)Members[^1]!["id"]!:D}");
    }

    [Fact]
    public void AMemberWithoutAnAddressIsNotAddedAndOneTheServerRefusesShowsWhy()
    {
        var app = Load("members");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));

        Section(app, "Add a member").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Section(app, "Add a member").TextContent.ShouldContain("Enter the email address of the member."));
        Management.Calls.ShouldNotContain(call => call.Method == HttpMethod.Post);

        Management.Answer = (method, path) => method == HttpMethod.Post && path == "members" ? Problem(HttpStatusCode.Conflict, "FCMS3163", "email") : null;
        Section(app, "Add a member").QuerySelector("input[type=email]")!.Input("ada@example.com");
        Section(app, "Add a member").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Section(app, "Add a member").TextContent.ShouldContain("A member has this email address already."));
        Navigation.Uri.ShouldEndWith("/members");

        Management.Answer = (method, path) => method == HttpMethod.Post && path == "members" ? Problem(HttpStatusCode.UnprocessableEntity, "FCMS3173", "") : null;
        Section(app, "Add a member").QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(app, "Add a member").ShouldStartWith("Not added: the link that lets a new member choose its password has nowhere to go."));
    }

    // members.manage held through a role of a site opens no call of the area, so neither page is shown and nothing is read.
    [Theory]
    [InlineData("members")]
    [InlineData("members/groups")]
    public void ACallerWhoHoldsMembersManageInASiteOnlyGetsNoAccessAndNoRead(string route)
    {
        GrantArea("members.manage", instanceOnly: false);

        var app = Load(route);

        app.WaitForAssertion(() => Heading(app).ShouldBe(Text("Shell.NoAccess.Heading")));
        Management.Calls.ShouldNotContain(call => call.Path.StartsWith("member", StringComparison.Ordinal));
        Loader.Loads.ShouldBe(0);
        Links(app).ShouldNotContain("members");
    }
}
