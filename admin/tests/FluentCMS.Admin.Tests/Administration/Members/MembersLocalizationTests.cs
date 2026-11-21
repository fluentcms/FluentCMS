using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Members;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Members;

// The check of spec 3.1 on the pages of the area: under a pseudo-culture no text is left untranslated. The area's issue
// adds each of its routes here, with the answers of the fake Management API that its page reads, so that the page is
// checked with its data and not only in its error state.
public sealed class MembersLocalizationTests : MembersTestContext
{
    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheMembersPageComesFromTheResources(string culture)
    {
        MarkData(culture);
        MemberOf(Grace)["deactivatedAt"] = Created;
        MemberOf(Grace)["lockoutEnd"] = "2099-01-01T00:00:00+00:00";
        MemberOf(Ada)["lastLoginAt"] = Created;

        var app = LoadLocalized(culture, "members", shown =>
        {
            shown.FindAll("main table tbody tr").Count.ShouldBe(2);
            shown.FindAll("main [role=group] select").Count.ShouldBe(3);
        });

        // The heading of an area's first page is {Area}.Page.Heading of its resources, which the end-to-end tests find it by.
        Heading(app).ShouldBe(Localized<MembersResources>("Members.Page.Heading"));
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfAMembersPageComesFromTheResources(string culture)
    {
        MarkData(culture);
        MemberOf(Ada)["lockoutEnd"] = "2099-01-01T00:00:00+00:00";
        Sessions[Ada].Add(new JsonObject { ["id"] = Guid.NewGuid(), ["clientId"] = null, ["clientName"] = null, ["createdAt"] = null, ["scopes"] = new JsonArray() });

        var app = LoadLocalized(culture, $"members/{Ada:D}", shown =>
        {
            Items(Section(shown, Localized<MembersResources>("Members.Sessions.Heading"))).Length.ShouldBe(2);
            Items(Section(shown, Localized<MembersResources>("Members.Logins.Heading"))).ShouldHaveSingleItem();
            Section(shown, Localized<MembersResources>("Members.Groups.Heading")).QuerySelector("select").ShouldNotBeNull();
        });

        // The question that names the member before it is deleted.
        Button(Section(app, Localized<MembersResources>("Members.Detail.Account")), Localized<MembersResources>("Members.Detail.Delete")).Click();
        app.WaitForAssertion(() => Button(Section(app, Localized<MembersResources>("Members.Detail.Account")), Localized<MembersResources>("Members.Detail.DeleteConfirm")));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheGroupsPageComesFromTheResources(string culture)
    {
        MarkData(culture);
        GroupOf(Readers)["description"] = PseudoCulture.Transform("People who read", culture == PseudoCulture.RightToLeft);

        var app = LoadLocalized(culture, "members/groups", shown =>
        {
            Items(Section(shown, Localized<MembersResources>("Members.GroupList.Heading"))).Length.ShouldBe(2);
            shown.Find("main form select").ShouldNotBeNull();
        });

        var list = Section(app, Localized<MembersResources>("Members.GroupList.Heading"));
        Button(list, Localized<MembersResources>("Members.GroupList.Delete") + Localized<MembersResources>("Members.GroupList.Which", Mark("writers", culture))).Click();
        app.WaitForAssertion(() => Button(Section(app, Localized<MembersResources>("Members.GroupList.Heading")), Localized<MembersResources>("Members.GroupList.DeleteConfirm")));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfARefusalComesFromTheResources(string culture)
    {
        MarkData(culture);
        Management.Answer = (method, path) => method == HttpMethod.Put && path.Contains("/groups/", StringComparison.Ordinal)
            ? Problem(HttpStatusCode.Forbidden, "FCMS3103")
            : null;
        var app = LoadLocalized(culture, $"members/{Grace:D}", shown => Section(shown, Localized<MembersResources>("Members.Groups.Heading")).QuerySelector("select").ShouldNotBeNull());
        var groups = Localized<MembersResources>("Members.Groups.Heading");

        Section(app, groups).QuerySelector("select")!.Change(Writers.ToString("D"));
        Button(Section(app, groups), Localized<MembersResources>("Members.Groups.AddButton")).Click();

        app.WaitForAssertion(() => Message(app, groups).ShouldBe(Localized<MembersResources>("Members.Groups.AboveReach")));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    private static string Mark(string value, string culture) => PseudoCulture.Transform(value, culture == PseudoCulture.RightToLeft);

    // Data the fake serves is marked as a resource is, so that only a text of the page itself can fail the check.
    private void MarkData(string culture)
    {
        foreach (var role in Roles)
        {
            role!["name"] = Mark((string)role["name"]!, culture);
        }

        foreach (var group in Groups)
        {
            group!["name"] = Mark((string)group["name"]!, culture);
            group["roleName"] = Mark((string)group["roleName"]!, culture);
        }

        foreach (var member in Members)
        {
            member!["email"] = Mark((string)member["email"]!, culture);
            if (member["displayName"] is { } name)
            {
                member["displayName"] = Mark((string)name!, culture);
            }

            foreach (var group in member["groups"]!.AsArray())
            {
                group!["name"] = Mark((string)group["name"]!, culture);
            }
        }

        Sessions[Ada][0]!["clientName"] = Mark("Shop", culture);
        Logins[Ada][0]!["displayName"] = Mark("GitHub", culture);
        Management.Sites[0]!["name"] = Mark((string)Management.Sites[0]!["name"]!, culture);
    }
}
