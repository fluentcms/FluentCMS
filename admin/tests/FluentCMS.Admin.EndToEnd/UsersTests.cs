using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The users pages of A-31 on the reference host, by keyboard: a Super Admin invites a user with a role, sees the invitation
// pending and revokes it, and finds another user by its address and changes its roles. The host sends the invitation to a
// mail server of the run's own (SmtpSink).
public sealed class UsersTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task AnAdministratorInvitesAUserWithARoleSeesItPendingAndRevokesIt()
    {
        var address = $"{Mark}@example.com";
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "users", await Host.CreateEditorAsync("Super Admin"));
        await Expect(Heading(tab)).ToBeVisibleAsync();
        var invite = Region(tab, "Users.Invite.Heading");

        await Ui.FocusAsync(invite.GetByLabel(Text("Users.Invite.Email")));
        await tab.Page.Keyboard.TypeAsync(address);
        await invite.GetByLabel(Text("Users.Invite.Role"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Editor" });
        await Ui.PressAsync(Ui.ButtonIn(invite, Text("Users.Invite.AddRole")), "Enter");
        await Expect(invite.Locator("li")).ToHaveCountAsync(1);
        await Expect(invite.Locator("li")).ToContainTextAsync("Editor");

        var sent = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(invite, Text("Users.Invite.Send")), "Enter"), "POST", tab.ManagementUrl("invitations"));
        sent.Status.ShouldBe(201);

        var pending = Region(tab, "Users.Invitations.Heading");
        var row = pending.Locator("li").Filter(new LocatorFilterOptions { HasText = address });
        await Expect(row).ToContainTextAsync(Ui.Format(Text("Users.Invitations.Roles"), "Editor"));
        await Expect(invite.Locator("li")).ToHaveCountAsync(0);

        var revoked = await tab.AnswerAsync(
            () => Ui.PressAsync(Ui.ButtonIn(pending, Text("Users.Invitations.Revoke") + Ui.Format(Text("Users.Invitations.RevokeWhich"), address)), "Enter"),
            response => response.Request.Method == "DELETE" && response.Url.StartsWith(tab.ManagementUrl("invitations/"), StringComparison.Ordinal));
        revoked.Status.ShouldBe(204);
        await Expect(row).ToHaveCountAsync(0);
        await Expect(pending.Locator(".users-message")).ToHaveTextAsync(Ui.Format(Text("Users.Invitations.Revoked"), address));
        await Expect(pending.Locator(".users-message")).ToBeFocusedAsync();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAdministratorFindsAnotherUserByItsAddressAndChangesItsRoles()
    {
        var other = await Host.CreateEditorAsync();
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "users", await Host.CreateEditorAsync("Super Admin"));
        await Expect(Heading(tab)).ToBeVisibleAsync();

        await Ui.FocusAsync(tab.Page.Locator("main").GetByLabel(Text("Users.List.Search"), new LocatorGetByLabelOptions { Exact = true }));
        await tab.Page.Keyboard.TypeAsync(other.Email);
        await tab.Page.Keyboard.PressAsync("Enter");
        var rows = tab.Page.Locator("main table tbody tr");
        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows).ToContainTextAsync(other.Email);

        // A row is one tab stop, and Enter opens the user.
        await Ui.PressAsync(rows, "Enter");
        await Expect(tab.Page.Locator("main h1")).ToHaveTextAsync(other.Email);
        var id = new Uri(tab.Page.Url).Segments[^1];

        var roles = Region(tab, "Users.Roles.Heading");
        await roles.GetByLabel(Text("Users.Roles.Add"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Reader" });
        var added = await tab.AnswerAsync(
            () => Ui.PressAsync(Ui.ButtonIn(roles, Text("Users.Roles.AddButton")), "Enter"),
            response => response.Request.Method == "PUT" && response.Url.StartsWith(tab.ManagementUrl($"users/{id}/roles/"), StringComparison.Ordinal));
        added.Status.ShouldBe(200);
        await Expect(roles.Locator(".users-message")).ToHaveTextAsync(Text("Users.Roles.Added"));

        var removeEditor = Ui.ButtonIn(roles, Text("Users.Roles.Remove") + Ui.Format(Text("Users.Roles.RemoveWhich"), "Editor"));
        var removed = await tab.AnswerAsync(
            () => Ui.PressAsync(removeEditor, "Enter"),
            response => response.Request.Method == "DELETE" && response.Url.StartsWith(tab.ManagementUrl($"users/{id}/roles/"), StringComparison.Ordinal));
        removed.Status.ShouldBe(200);
        await Expect(removeEditor).ToHaveCountAsync(0);
        await Expect(roles.Locator(".users-message")).ToBeFocusedAsync();

        using var management = await Host.ManagementClientAsync();
        var user = await management.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/users/{id}", UriKind.Relative), TestContext.Current.CancellationToken);
        user!["roles"]!.AsArray().Select(role => (string?)role!["name"]).ShouldBe(["Reader"]);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key) => Repository.AdministrationText("Users", key);

    private static ILocator Heading(AdminTab tab) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Repository.AdministrationHeading("Users"), Exact = true, Level = 1 });

    // A card of the page, a region named by its heading.
    private static ILocator Region(AdminTab tab, string key) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Region, new LocatorGetByRoleOptions { Name = Text(key), Exact = true });
}
