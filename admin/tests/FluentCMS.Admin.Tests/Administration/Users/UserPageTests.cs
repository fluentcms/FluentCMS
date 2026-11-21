using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Users;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Tests.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Users;

// A user's page: its state and the actions on it, its roles, sessions and external logins, and each refusal by its meaning.
public sealed class UserPageTests : UsersTestContext
{
    public UserPageTests()
    {
        UseNeutralResources();
        GrantArea("users.manage", instanceOnly: true);
    }

    private string When(string moment) => Services.GetRequiredService<FormatService>().DateTime(DateTimeOffset.Parse(moment, System.Globalization.CultureInfo.InvariantCulture));

    private IRenderedComponent<AdminApp> Open(Guid user)
    {
        var app = Load($"users/{user:D}");
        app.WaitForAssertion(() =>
        {
            Section(app, "Sessions").QuerySelector("[role=status]").ShouldBeNull();
            Section(app, "External logins").QuerySelector("[role=status]").ShouldBeNull();
            Section(app, "Roles").QuerySelector("select").ShouldNotBeNull();
        });
        return app;
    }

    private static string Account(IRenderedComponent<AdminApp> app) => Section(app, "Account").QuerySelector("dl")!.TextContent;

    [Fact]
    public void TheUserShowsItsStateItsRolesItsSessionsAndItsLogins()
    {
        UserOf(Grace)["lockoutEnd"] = "2099-01-01T00:00:00+00:00";

        var app = Open(Grace);

        Heading(app).ShouldBe("grace");
        app.Find("main .cms-breadcrumb [aria-current=page]").TextContent.ShouldBe("grace");
        var account = Account(app);
        account.ShouldContain("grace@example.com");
        account.ShouldContain("Active");
        account.ShouldContain($"Locked out until {When("2099-01-01T00:00:00+00:00")}. A lockout ends by itself; no action ends it early.");
        account.ShouldContain("Off");
        account.ShouldContain("No change asked for");
        Buttons(Section(app, "Account")).ShouldBe(["Deactivate", "Require a password change"]);
        Items(Section(app, "Roles")).ShouldBe(["Editor", "Author (Main)"]);
        Items(Section(app, "Sessions")).ShouldBe([$"FluentCMS admin, signed in {When(Created)}"]);
        Items(Section(app, "External logins")).ShouldBe(["GitHub"]);
    }

    [Fact]
    public void ARoleIsAddedAndRemovedAndTheRolesHeldAreNotOffered()
    {
        var app = Open(Grace);
        Section(app, "Roles").QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["Choose a role", "Super Admin"]);

        Section(app, "Roles").QuerySelector("select")!.Change(SuperAdmin.ToString("D"));
        Button(Section(app, "Roles"), "Add the role").Click();

        app.WaitForAssertion(() => Items(Section(app, "Roles")).ShouldBe(["Editor", "Author (Main)", "Super Admin"]));
        Message(app, "Roles").ShouldBe("The role was added.");
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Put && call.Path == $"users/{Grace:D}/roles/{SuperAdmin:D}");

        Button(Section(app, "Roles"), "Remove the role Author (Main)").Click();

        app.WaitForAssertion(() => Items(Section(app, "Roles")).ShouldBe(["Editor", "Super Admin"]));
        Message(app, "Roles").ShouldBe("The role Author (Main) was removed.");
        app.WaitForAssertion(() => ShouldBeFocused<UserPage>(app, "rolesElement"));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == $"users/{Grace:D}/roles/{Author:D}");
    }

    [Fact]
    public void DeactivateAsksFirstAndTheUserIsReactivatedAgain()
    {
        var app = Open(Grace);

        Button(Section(app, "Account"), "Deactivate").Click();

        app.WaitForAssertion(() => Section(app, "Account").TextContent.ShouldContain("Deactivate grace? The user's sessions end"));
        app.WaitForAssertion(() => ShouldBeFocused<UserPage>(app, "questionElement"));
        Button(Section(app, "Account"), "Cancel").Click();
        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("Nothing was changed."));
        app.WaitForAssertion(() => ShouldBeFocused<UserPage>(app, "accountElement"));
        Management.Calls.ShouldNotContain(call => call.Path.EndsWith("/deactivate", StringComparison.Ordinal));

        Button(Section(app, "Account"), "Deactivate").Click();
        app.WaitForAssertion(() => Button(Section(app, "Account"), "Deactivate the user"));
        Button(Section(app, "Account"), "Deactivate the user").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The user was deactivated."));
        Account(app).ShouldContain($"Deactivated on {When("2026-11-20T10:00:00+00:00")}");

        Button(Section(app, "Account"), "Reactivate").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The user was reactivated."));
        Account(app).ShouldContain("Active");
        Buttons(Section(app, "Account")).ShouldContain("Deactivate");
    }

    // Deactivate, reactivate, the two-factor reset and the removal of a login end the user's sessions on the server, so the
    // card reads them again after each and lists none of those ended.
    [Fact]
    public void TheSessionsAreReadAgainAfterEachChangeThatEndsThem()
    {
        UserOf(Grace)["twoFactorEnabled"] = true;
        var app = Open(Grace);
        var sessions = $"users/{Grace:D}/sessions";
        Items(Section(app, "Sessions")).ShouldHaveSingleItem();

        Button(Section(app, "Account"), "Deactivate").Click();
        app.WaitForAssertion(() => Button(Section(app, "Account"), "Deactivate the user"));
        Button(Section(app, "Account"), "Deactivate the user").Click();
        app.WaitForAssertion(() => Section(app, "Sessions").TextContent.ShouldContain("The user is signed in nowhere."));
        Management.Count(sessions).ShouldBe(2);

        Sessions[Grace].Add(new JsonObject { ["id"] = Guid.NewGuid(), ["clientId"] = "cli", ["clientName"] = "CLI", ["createdAt"] = Created, ["scopes"] = new JsonArray() });
        Button(Section(app, "Account"), "Reactivate").Click();
        app.WaitForAssertion(() => Management.Count(sessions).ShouldBe(3));
        app.WaitForAssertion(() => Section(app, "Sessions").TextContent.ShouldContain("The user is signed in nowhere."));

        Sessions[Grace].Add(new JsonObject { ["id"] = Guid.NewGuid(), ["clientId"] = "cli", ["clientName"] = "CLI", ["createdAt"] = Created, ["scopes"] = new JsonArray() });
        Button(Section(app, "Account"), "Reset two-factor sign-in").Click();
        app.WaitForAssertion(() => Button(Section(app, "Account"), "Cancel"));
        Button(Section(app, "Account"), "Reset two-factor sign-in").Click();
        app.WaitForAssertion(() => Management.Count(sessions).ShouldBe(4));
        app.WaitForAssertion(() => Section(app, "Sessions").TextContent.ShouldContain("The user is signed in nowhere."));

        Sessions[Grace].Add(new JsonObject { ["id"] = Guid.NewGuid(), ["clientId"] = "cli", ["clientName"] = "CLI", ["createdAt"] = Created, ["scopes"] = new JsonArray() });
        Button(Section(app, "External logins"), "Remove the login through GitHub").Click();
        app.WaitForAssertion(() => Management.Count(sessions).ShouldBe(5));
        app.WaitForAssertion(() => Section(app, "Sessions").TextContent.ShouldContain("The user is signed in nowhere."));
    }

    [Fact]
    public void ASessionIsEndedAndAnExternalLoginRemovedWithFocusOnTheResult()
    {
        var app = Open(Grace);

        Button(Section(app, "Sessions"), $"End the session of FluentCMS admin signed in {When(Created)}").Click();

        app.WaitForAssertion(() => Message(app, "Sessions").ShouldBe("The session of FluentCMS admin was ended."));
        Section(app, "Sessions").TextContent.ShouldContain("The user is signed in nowhere.");
        app.WaitForAssertion(() => ShouldBeFocused<UserSessions>(app, "messageElement"));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == $"users/{Grace:D}/sessions/{Laptop:D}");

        Button(Section(app, "External logins"), "Remove the login through GitHub").Click();

        app.WaitForAssertion(() => Message(app, "External logins").ShouldBe("The login through GitHub was removed."));
        Section(app, "External logins").TextContent.ShouldContain("The user signs in through no provider.");
        app.WaitForAssertion(() => ShouldBeFocused<UserLogins>(app, "messageElement"));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == $"users/{Grace:D}/logins/GitHub");
    }

    [Fact]
    public void TheTwoFactorResetAsksFirstAndTurnsItOff()
    {
        UserOf(Grace)["twoFactorEnabled"] = true;
        var app = Open(Grace);

        Button(Section(app, "Account"), "Reset two-factor sign-in").Click();
        app.WaitForAssertion(() => Section(app, "Account").TextContent.ShouldContain("Reset the two-factor sign-in of grace?"));
        Buttons(Section(app, "Account")).ShouldBe(["Reset two-factor sign-in", "Cancel"]);
        Button(Section(app, "Account"), "Reset two-factor sign-in").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The two-factor sign-in of the user was reset."));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Post && call.Path == $"users/{Grace:D}/two-factor/reset");
        Buttons(Section(app, "Account")).ShouldNotContain("Reset two-factor sign-in");
    }

    // The server takes a reset only from a user signed in with a second factor, which the page cannot know before.
    [Fact]
    public void ATwoFactorResetWithoutASecondFactorShowsTheWayToSignInAgainWithOne()
    {
        UserOf(Grace)["twoFactorEnabled"] = true;
        Management.Answer = (method, path) => path.EndsWith("/two-factor/reset", StringComparison.Ordinal) ? Problem(HttpStatusCode.Forbidden, "FCMS3064", "") : null;
        var app = Open(Grace);

        Button(Section(app, "Account"), "Reset two-factor sign-in").Click();
        app.WaitForAssertion(() => Buttons(Section(app, "Account")).ShouldBe(["Reset two-factor sign-in", "Cancel"]));
        Button(Section(app, "Account"), "Reset two-factor sign-in").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldStartWith("Not reset: resetting another user's two-factor sign-in needs you signed in with a second factor."));
        Section(app, "Account").QuerySelector("a[href='account']")!.TextContent.ShouldBe("Your account");
        Account(app).ShouldContain("On");

        Button(Section(app, "Account"), "Sign out").Click();

        ((SignedInTestSession)Services.GetRequiredService<IAdminSession>()).SignOuts.ShouldBe(1);
    }

    // A change of a user needs every grant of its roles, and giving or taking a role every grant of that role.
    [Fact]
    public void ACallerBelowTheUsersRolesSeesTheRefusalNamed()
    {
        Management.Answer = (method, path) => method == HttpMethod.Get ? null : Problem(HttpStatusCode.Forbidden, "FCMS3103");
        var app = Open(Grace);

        Button(Section(app, "Account"), "Require a password change").Click();
        app.WaitForAssertion(() => Message(app, "Account").ShouldBe(
            "Not changed: the user holds a role that gives a permission you do not hold. A change of a user needs every grant of its roles."));

        Section(app, "Roles").QuerySelector("select")!.Change(SuperAdmin.ToString("D"));
        Button(Section(app, "Roles"), "Add the role").Click();
        app.WaitForAssertion(() => Message(app, "Roles").ShouldBe("Not changed: the role gives a permission you do not hold. Giving or taking a role needs every grant of it."));
        Items(Section(app, "Roles")).ShouldBe(["Editor", "Author (Main)"]);

        Button(Section(app, "Sessions"), $"End the session of FluentCMS admin signed in {When(Created)}").Click();
        app.WaitForAssertion(() => Message(app, "Sessions").ShouldStartWith("Not changed: the user holds a role that gives a permission you do not hold."));

        Button(Section(app, "External logins"), "Remove the login through GitHub").Click();
        app.WaitForAssertion(() => Message(app, "External logins").ShouldStartWith("Not changed: the user holds a role that gives a permission you do not hold."));
        Items(Section(app, "External logins")).ShouldBe(["GitHub"]);
    }

    [Fact]
    public void TheLastSuperAdminIsNeitherDeactivatedNorLosesItsRoleAndThePageSaysWhy()
    {
        Management.Answer = (method, path) => path.EndsWith("/deactivate", StringComparison.Ordinal) || method == HttpMethod.Delete
            ? Problem(HttpStatusCode.Conflict, "FCMS3052", "id")
            : null;
        var app = Open(Ada);

        Button(Section(app, "Roles"), "Remove the role Super Admin").Click();
        app.WaitForAssertion(() => Message(app, "Roles").ShouldBe("Not removed: this is the last active Super Admin, and the instance would be left without one."));
        Items(Section(app, "Roles")).ShouldBe(["Super Admin"]);

        Button(Section(app, "Account"), "Deactivate").Click();
        app.WaitForAssertion(() => Button(Section(app, "Account"), "Deactivate the user"));
        Button(Section(app, "Account"), "Deactivate the user").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("Not changed: this is the last active Super Admin, and the instance would be left without one."));
        Account(app).ShouldContain("Active");
    }

    // The answer does not say whether a user has a password; the server refuses the change for one without (#3378).
    [Fact]
    public void TheForcedPasswordChangeIsSetAndWithdrawnAndRefusedForAUserWithoutAPassword()
    {
        var app = Open(Grace);
        Section(app, "Account").TextContent.ShouldContain("This needs a password: a user who signs in through a provider only has none and is refused.");

        Button(Section(app, "Account"), "Require a password change").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The user must change its password at its next sign-in."));
        JsonNode.Parse(BodyOf(HttpMethod.Patch, $"users/{Grace:D}")!)!.ToJsonString().ShouldBe("""{"mustChangePassword":true}""");
        Account(app).ShouldContain("Must be changed at the next sign-in");

        Button(Section(app, "Account"), "Withdraw the password change").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("The user no longer has to change its password."));
        JsonNode.Parse(BodyOf(HttpMethod.Patch, $"users/{Grace:D}")!)!.ToJsonString().ShouldBe("""{"mustChangePassword":false}""");

        Management.Answer = (method, _) => method == HttpMethod.Patch ? Problem(HttpStatusCode.UnprocessableEntity, "FCMS3049", "mustChangePassword") : null;
        Button(Section(app, "Account"), "Require a password change").Click();

        app.WaitForAssertion(() => Message(app, "Account").ShouldBe("Not changed: the user has no password, since it signs in through a provider only."));
        Account(app).ShouldContain("No change asked for");
    }

    [Fact]
    public void TheCallersOwnUserIsOfferedNeitherDeactivateNorTheTwoFactorReset()
    {
        Management.UserId = Ada;

        var app = Open(Ada);

        Buttons(Section(app, "Account")).ShouldBe(["Require a password change"]);
    }

    [Fact]
    public void ALastWayToSignInIsKeptAndThePageSaysWhy()
    {
        Management.Answer = (method, _) => method == HttpMethod.Delete ? Problem(HttpStatusCode.Conflict, "FCMS3090") : null;
        var app = Open(Grace);

        Button(Section(app, "External logins"), "Remove the login through GitHub").Click();

        app.WaitForAssertion(() => Message(app, "External logins").ShouldBe("Not removed: the login through GitHub is the user's last way to sign in."));
        Items(Section(app, "External logins")).ShouldBe(["GitHub"]);
    }

    [Fact]
    public void AnUnknownUserIsNotFoundWithAWayBackToTheList()
    {
        var app = Load($"users/{Guid.NewGuid():D}");

        app.WaitForAssertion(() => Heading(app).ShouldBe("User not found"));
        app.Find("main p a[href='users']").TextContent.ShouldBe("Back to the users");
    }

    // users.manage held through a role of a site opens no call of the area, so the page is not shown and reads nothing.
    [Fact]
    public void ACallerWhoHoldsUsersManageInASiteOnlyGetsNoAccessAndNoRead()
    {
        GrantArea("users.manage", instanceOnly: false);

        var app = Load($"users/{Grace:D}");

        app.WaitForAssertion(() => Heading(app).ShouldBe(Text("Shell.NoAccess.Heading")));
        Management.Calls.ShouldNotContain(call => call.Path.StartsWith("users", StringComparison.Ordinal));
        Loader.Loads.ShouldBe(0);
        Links(app).ShouldNotContain("users");
    }
}
