using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Users;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Users;

// The check of spec 3.1 on the pages of the area: under a pseudo-culture no text is left untranslated. The area's issue
// adds each of its routes here, with the answers of the fake Management API that its page reads, so that the page is
// checked with its data and not only in its error state.
public sealed class UsersLocalizationTests : UsersTestContext
{
    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheUsersPageComesFromTheResources(string culture)
    {
        MarkData(culture);
        UserOf(Grace)["lockoutEnd"] = "2099-01-01T00:00:00+00:00";
        UserOf(Grace)["mustChangePassword"] = true;
        Invitations.Add(new JsonObject
        {
            ["id"] = Laptop,
            ["email"] = PseudoCulture.Transform("sam@example.com", culture == PseudoCulture.RightToLeft),
            ["roleIds"] = new JsonArray(Editor, Guid.NewGuid()),
            ["createdAt"] = Created,
            ["createdBy"] = Ada,
            ["expiresAt"] = "2026-11-09T08:00:00+00:00",
        });

        var app = LoadLocalized(culture, "users", shown =>
        {
            shown.FindAll("main table tbody tr").Count.ShouldBe(2);
            Items(Section(shown, Localized<UsersResources>("Users.Invitations.Heading"))).ShouldHaveSingleItem();
            Section(shown, Localized<UsersResources>("Users.Invite.Heading")).QuerySelector("select").ShouldNotBeNull();
        });

        // The heading of an area's first page is {Area}.Page.Heading of its resources, which the end-to-end tests find it by.
        Heading(app).ShouldBe(Localized<UsersResources>("Users.Page.Heading"));
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfAUsersPageComesFromTheResources(string culture)
    {
        MarkData(culture);
        UserOf(Grace)["lockoutEnd"] = "2099-01-01T00:00:00+00:00";
        Sessions[Grace].Add(new JsonObject { ["id"] = Guid.NewGuid(), ["clientId"] = null, ["clientName"] = null, ["createdAt"] = null, ["scopes"] = new JsonArray() });

        LoadLocalized(culture, $"users/{Grace:D}", shown =>
        {
            Items(Section(shown, Localized<UsersResources>("Users.Sessions.Heading"))).Length.ShouldBe(2);
            Items(Section(shown, Localized<UsersResources>("Users.Logins.Heading"))).ShouldHaveSingleItem();
            Section(shown, Localized<UsersResources>("Users.Roles.Heading")).QuerySelector("select").ShouldNotBeNull();
        });
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfARefusalComesFromTheResources(string culture)
    {
        MarkData(culture);
        UserOf(Grace)["twoFactorEnabled"] = true;
        Management.Answer = (method, path) => path.EndsWith("/two-factor/reset", StringComparison.Ordinal)
            ? Problem(HttpStatusCode.Forbidden, "FCMS3064", "")
            : null;
        var app = LoadLocalized(culture, $"users/{Grace:D}", shown => Section(shown, Localized<UsersResources>("Users.Roles.Heading")).QuerySelector("select").ShouldNotBeNull());

        Button(Section(app, Localized<UsersResources>("Users.Detail.Account")), Localized<UsersResources>("Users.Detail.ResetTwoFactor")).Click();
        app.WaitForAssertion(() => Button(Section(app, Localized<UsersResources>("Users.Detail.Account")), Localized<UsersResources>("Users.Detail.ResetConfirm")));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
        Button(Section(app, Localized<UsersResources>("Users.Detail.Account")), Localized<UsersResources>("Users.Detail.ResetConfirm")).Click();

        app.WaitForAssertion(() => Message(app, Localized<UsersResources>("Users.Detail.Account")).ShouldBe(Localized<UsersResources>("Users.Detail.SecondFactorRequired")));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    // Data the fake serves is marked as a resource is, so that only a text of the page itself can fail the check.
    private void MarkData(string culture)
    {
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        string Mark(string value) => PseudoCulture.Transform(value, rightToLeft);

        foreach (var role in Roles)
        {
            role!["name"] = Mark((string)role["name"]!);
        }

        foreach (var user in Users)
        {
            user!["userName"] = Mark((string)user["userName"]!);
            user["email"] = Mark((string)user["email"]!);
            foreach (var role in user["roles"]!.AsArray())
            {
                role!["name"] = Mark((string)role["name"]!);
            }
        }

        Sessions[Grace][0]!["clientName"] = Mark("FluentCMS admin");
        Logins[Grace][0]!["displayName"] = Mark("GitHub");
        Management.Sites[0]!["name"] = Mark((string)Management.Sites[0]!["name"]!);
    }
}
