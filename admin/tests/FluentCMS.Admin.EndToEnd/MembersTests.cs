using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The members pages of A-40 on the reference host, by keyboard: a Super Admin makes a group bound to a role, adds a member,
// puts it in the group, finds it by the group, takes it out again and deletes it after the question that names it. The host
// mails the new member its link to a mail server of the run's own (SmtpSink).
public sealed class MembersTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task AnAdministratorMakesAGroupPutsAMemberInItFindsItByTheGroupAndTakesItOutAgain()
    {
        var (group, address) = (Mark, $"{Mark}@example.com");
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "members/groups", await Host.CreateEditorAsync("Super Admin"));
        var main = tab.Page.Locator("main");
        await Expect(main.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Text("Members.GroupsPage.Heading"), Exact = true, Level = 1 })).ToBeVisibleAsync();

        // A group bound to a role.
        var form = Region(tab, "Members.GroupForm.NewHeading");
        await Ui.FocusAsync(form.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = Text("Members.GroupForm.Name"), Exact = true }));
        await tab.Page.Keyboard.TypeAsync(group);
        await form.GetByRole(AriaRole.Combobox, new LocatorGetByRoleOptions { Name = Text("Members.GroupForm.Role"), Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Reader" });
        var made = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(form, Text("Members.GroupForm.Create")), "Enter"), "POST", tab.ManagementUrl("member-groups"));
        var groupId = await Ui.CreatedIdAsync(made, "member-groups");
        var listed = Region(tab, "Members.GroupList.Heading").Locator("li").Filter(new LocatorFilterOptions { HasText = group });
        await Expect(listed).ToContainTextAsync(Ui.Format(Text("Members.GroupList.Role"), "Reader"));

        // A member, added on the members page, whose own page opens.
        await Ui.PressAsync(Entry(tab), "Enter");
        await Expect(Heading(tab)).ToBeVisibleAsync();
        var add = Region(tab, "Members.Add.Heading");
        await Ui.FocusAsync(add.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = Text("Members.Add.Email"), Exact = true }));
        await tab.Page.Keyboard.TypeAsync(address);
        var added = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(add, Text("Members.Add.Submit")), "Enter"), "POST", tab.ManagementUrl("members"));
        var memberId = await Ui.CreatedIdAsync(added, "members");
        await Expect(main.Locator("h1")).ToHaveTextAsync(address);

        // Put in the group.
        var groups = Region(tab, "Members.Groups.Heading");
        await groups.GetByLabel(Text("Members.Groups.Add"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = group });
        var put = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(groups, Text("Members.Groups.AddButton")), "Enter"), "PUT", tab.ManagementUrl($"members/{memberId:D}/groups/{groupId:D}"));
        put.Status.ShouldBe(200);
        await Expect(groups.Locator(".members-message")).ToHaveTextAsync(Ui.Format(Text("Members.Groups.Added"), group));
        await Expect(groups.Locator("li")).ToHaveCountAsync(1);

        // Found on the list by the group.
        await Ui.PressAsync(Entry(tab), "Enter");
        await Expect(Heading(tab)).ToBeVisibleAsync();
        await main.GetByRole(AriaRole.Group, new LocatorGetByRoleOptions { Name = Text("Members.Filters.Label"), Exact = true })
            .GetByLabel(Text("Members.Filters.Group"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = group });
        var rows = main.Locator("table tbody tr");
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, $"members?group={groupId:D}").AbsoluteUri);
        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows).ToContainTextAsync(address);

        // A row is one tab stop, and Enter opens the member, which is taken out of the group.
        await Ui.PressAsync(rows, "Enter");
        await Expect(main.Locator("h1")).ToHaveTextAsync(address);
        var takeOut = Ui.ButtonIn(groups, Text("Members.Groups.Remove") + Ui.Format(Text("Members.Groups.RemoveWhich"), group));
        var removed = await tab.AnswerAsync(() => Ui.PressAsync(takeOut, "Enter"), "DELETE", tab.ManagementUrl($"members/{memberId:D}/groups/{groupId:D}"));
        removed.Status.ShouldBe(200);
        await Expect(takeOut).ToHaveCountAsync(0);
        await Expect(groups.Locator(".members-message")).ToBeFocusedAsync();

        // Deleted after the question that names it.
        var account = Region(tab, "Members.Detail.Account");
        await Ui.PressAsync(Ui.ButtonIn(account, Text("Members.Detail.Delete")), "Enter");
        await Expect(account.GetByText(Ui.Format(Text("Members.Detail.DeleteQuestion"), address))).ToBeFocusedAsync();
        var deleted = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(account, Text("Members.Detail.DeleteConfirm")), "Enter"), "DELETE", tab.ManagementUrl($"members/{memberId:D}"));
        deleted.Status.ShouldBe(204);
        await Expect(Heading(tab)).ToBeVisibleAsync();

        using var management = await Host.ManagementClientAsync();
        var token = TestContext.Current.CancellationToken;
        using var gone = await management.GetAsync(new Uri($"api/management/v1/members/{memberId:D}", UriKind.Relative), token);
        gone.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        var stored = await management.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/member-groups/{groupId:D}", UriKind.Relative), token);
        stored!["roleName"]!.GetValue<string>().ShouldBe("Reader");
        stored["members"]!.GetValue<int>().ShouldBe(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key) => Repository.AdministrationText("Members", key);

    // The sidebar's entry of the area, which a keyboard user reaches the list by.
    private static ILocator Entry(AdminTab tab) =>
        tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText("Administration.Menu.Members"), Exact = true });

    private static ILocator Heading(AdminTab tab) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Repository.AdministrationHeading("Members"), Exact = true, Level = 1 });

    // A card of the page, a region named by its heading.
    private static ILocator Region(AdminTab tab, string key) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Region, new LocatorGetByRoleOptions { Name = Text(key), Exact = true });
}
