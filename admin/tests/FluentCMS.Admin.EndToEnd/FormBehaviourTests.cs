using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The behaviours of the form (A-49) on the Blog sample: a save that another tab made stale opens the conflict dialog, whose
// keep mine and take theirs end in the state they name, and leaving unsaved changes asks first for a route change, a closed
// tab and a sign-out (#3213), and not otherwise.
public sealed class FormBehaviourTests(EndToEndHost host) : BrowserTest(host)
{
    private const string ConflictTitle = "Content.Form.Conflict.Title";
    private const string LeaveTitle = "Content.Form.Leave.Title";
    private const string SignOutTitle = "Shell.SignOut.Title";

    [Fact]
    public async Task ASaveThatASecondTabMadeStaleOpensTheDialogAndKeepMineAndTakeTheirsEndInTheirStates()
    {
        var browser = await OpenBrowserAsync();
        var first = await browser.NewTabAsync();
        await first.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(first, Host.Editor);
        await Title(first).FillAsync("Read by two tabs");
        var create = await SaveAsync(first, () => first.Button("Content.Form.Save").ClickAsync(), "POST", first.ManagementUrl("content/article"));
        var location = (await create.HeaderValueAsync("location")).ShouldNotBeNull();
        var id = Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
        var entry = first.ManagementUrl($"content/article/{id:D}");
        await first.Page.WaitForURLAsync(new Regex($"/admin/content/article/{id:D}$"));

        // The second tab of the browser signs in through the server's session and saves first.
        var second = await browser.NewTabAsync();
        await second.Page.GotoAsync(new Uri(Host.Admin, $"content/article/{id:D}").AbsoluteUri);
        await Expect(Title(second)).ToHaveValueAsync("Read by two tabs");
        await Title(second).FillAsync("Saved by the second");
        (await SaveAsync(second, () => second.Button("Content.Form.Save").ClickAsync(), "PUT", entry)).Status.ShouldBe(200);

        await Title(first).FillAsync("Saved by the first");
        (await SaveAsync(first, () => first.Button("Content.Form.Save").ClickAsync(), "PUT", entry)).Status.ShouldBe(412);
        await Expect(Shown(first, ConflictTitle)).ToBeVisibleAsync();
        await Expect(Dialog(first, ConflictTitle).GetByText("Saved by the second", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();

        var kept = await SaveAsync(first, () => DialogButton(first, ConflictTitle, "Content.Form.Conflict.KeepMine").ClickAsync(), "PUT", entry);
        kept.Status.ShouldBe(200);
        (await kept.Request.HeaderValueAsync("if-match")).ShouldBe("\"2\"");
        await Expect(first.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Saved by the first", Exact = true })).ToBeVisibleAsync();
        await Expect(Shown(first, ConflictTitle)).ToBeHiddenAsync();

        // Now the second tab is stale: take theirs shows what the first saved, with nothing left to save.
        await Title(second).FillAsync("Saved by the second again");
        (await SaveAsync(second, () => second.Button("Content.Form.Save").ClickAsync(), "PUT", entry)).Status.ShouldBe(412);
        await DialogButton(second, ConflictTitle, "Content.Form.Conflict.TakeTheirs").ClickAsync();
        await Expect(Title(second)).ToHaveValueAsync("Saved by the first");
        await Expect(Shown(second, ConflictTitle)).ToBeHiddenAsync();
        await Home(second).ClickAsync();
        await second.Page.WaitForURLAsync(new Uri(Host.Admin, "").AbsoluteUri);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task LeavingUnsavedChangesAsksAtARouteChangeAClosedTabAndASignOutAndNotOtherwise()
    {
        var editor = await Host.CreateEditorAsync();
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var unloadQuestions = 0;
        tab.Page.Dialog += async (_, dialog) =>
        {
            if (dialog.Type == DialogType.BeforeUnload)
            {
                unloadQuestions++;
            }

            await dialog.DismissAsync();
        };
        var form = new Uri(Host.Admin, "content/article/new").AbsoluteUri;
        await tab.Page.GotoAsync(form);
        await browser.SignInOnTheHostedPageAsync(tab, editor);
        await Expect(Title(tab)).ToBeVisibleAsync();

        // Nothing changed: the route changes at once.
        await Home(tab).ClickAsync();
        await tab.Page.WaitForURLAsync(new Uri(Host.Admin, "").AbsoluteUri);
        await tab.Page.GoBackAsync();
        await Expect(Title(tab)).ToBeVisibleAsync();

        // A change, then the route asks, and staying keeps it.
        await Title(tab).ClickAsync();
        await Title(tab).PressSequentiallyAsync(" changed");
        await Home(tab).ClickAsync();
        await Expect(Shown(tab, LeaveTitle)).ToBeVisibleAsync();
        await DialogButton(tab, LeaveTitle, "Content.Form.Leave.Cancel").ClickAsync();
        await Expect(Shown(tab, LeaveTitle)).ToBeHiddenAsync();
        tab.Page.Url.ShouldBe(form);
        await Expect(Title(tab)).ToHaveValueAsync(new Regex(" changed$"));

        // The browser asks before the tab closes; dismissed, the tab stays.
        await tab.Page.CloseAsync(new PageCloseOptions { RunBeforeUnload = true });
        await UntilAsync(() => unloadQuestions == 1);
        tab.Page.IsClosed.ShouldBeFalse();

        // Sign-out asks too; signing out then leaves the page without the browser's question.
        await tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = editor.Email }).ClickAsync();
        await SignOut(tab).ClickAsync();
        await Expect(Shown(tab, SignOutTitle)).ToBeVisibleAsync();
        await DialogButton(tab, SignOutTitle, "Shell.SignOut.Cancel").ClickAsync();
        await Expect(Shown(tab, SignOutTitle)).ToBeHiddenAsync();
        tab.Page.Url.ShouldBe(form);

        await tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = editor.Email }).ClickAsync();
        await SignOut(tab).ClickAsync();
        await DialogButton(tab, SignOutTitle, "Shell.SignOut.Confirm").ClickAsync();
        await tab.Page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/account/logout");
        unloadQuestions.ShouldBe(1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task ATabWithoutUnsavedChangesClosesWithoutAQuestion()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var unloadQuestions = 0;
        tab.Page.Dialog += async (_, dialog) =>
        {
            unloadQuestions += dialog.Type == DialogType.BeforeUnload ? 1 : 0;
            await dialog.DismissAsync();
        };
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        await Title(tab).ClickAsync();
        await Title(tab).PressSequentiallyAsync(" saved");
        await SaveAsync(tab, () => tab.Button("Content.Form.Save").ClickAsync(), "POST", tab.ManagementUrl("content/article"));
        await tab.Page.WaitForURLAsync(new Regex("/admin/content/article/[0-9a-f-]{36}$"));

        await tab.Page.CloseAsync(new PageCloseOptions { RunBeforeUnload = true });

        await UntilAsync(() => tab.Page.IsClosed);
        unloadQuestions.ShouldBe(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static ILocator Title(AdminTab tab) => tab.Page.Locator("[data-field='title'] input");

    private static ILocator Home(AdminTab tab) => tab.Page.Locator("a.shell-home");

    private static ILocator SignOut(AdminTab tab) =>
        tab.Page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = Repository.AdminText("Shell.UserMenu.SignOut"), Exact = true });

    // The dialog as the accessibility tree has it: its host element has no box of its own, the library shows it in the top layer.
    private static ILocator Shown(AdminTab tab, string titleKey) =>
        tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = Repository.AdminText(titleKey), Exact = true });

    // The library's dialog is named by the title the wrapper sets on its host element, whose buttons are its light children.
    private static ILocator Dialog(AdminTab tab, string titleKey) =>
        tab.Page.Locator($"fluent-dialog[aria-label=\"{Repository.AdminText(titleKey)}\"]");

    private static ILocator DialogButton(AdminTab tab, string titleKey, string buttonKey) =>
        Dialog(tab, titleKey).Locator("fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText(buttonKey))}\s*$") });

    private static Task<IResponse> SaveAsync(AdminTab tab, Func<Task> action, string method, string url) =>
        tab.Page.RunAndWaitForResponseAsync(
            action,
            response => response.Request.Method == method && response.Url == url,
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });

    // A state that a browser event sets, polled within the harness's wait.
    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + AdminBrowser.Wait;
        while (!condition())
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
