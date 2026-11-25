using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The behaviours of the form (A-49) on the Blog sample: a save that another tab made stale opens the conflict dialog, whose
// keep mine and take theirs end in the state they name, and leaving unsaved changes asks first for a route change, a closed
// tab, a site switch and a sign-out (#3213), and not otherwise.
public sealed class FormBehaviourTests(EndToEndHost host) : BrowserTest(host)
{
    private const string ConflictTitle = "Content.Form.Conflict.Title";
    private const string LeaveTitle = "Content.Form.Leave.Title";
    private const string SignOutTitle = "Shell.SignOut.Title";
    private const string SwitchTitle = "Shell.SiteSwitcher.UnsavedTitle";

    private static PageAssertionsToHaveURLOptions UrlWait => new() { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds };

    [Fact]
    public async Task ASaveThatASecondTabMadeStaleOpensTheDialogAndKeepMineAndTakeTheirsEndInTheirStates()
    {
        var browser = await OpenBrowserAsync();
        var first = await SignedInTabAsync(browser, "content/article/new");
        await Title(first).FillAsync($"Read by two tabs {Mark}");
        var create = await SaveAsync(first, () => first.Button("Content.Form.Save").ClickAsync(), "POST", first.ManagementUrl("content/article"));
        var id = await Ui.CreatedIdAsync(create, "content/article");
        var entry = first.ManagementUrl($"content/article/{id:D}");
        await Expect(first.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}\\?locale=en$"), UrlWait);

        // The second tab of the browser signs in through the server's session and saves first.
        var second = await browser.NewTabAsync();
        await second.Page.GotoAsync(new Uri(Host.Admin, $"content/article/{id:D}").AbsoluteUri);
        await Expect(Title(second)).ToHaveValueAsync($"Read by two tabs {Mark}");
        await Title(second).FillAsync($"Saved by the second {Mark}");
        (await SaveAsync(second, () => second.Button("Content.Form.Save").ClickAsync(), "PUT", entry)).Status.ShouldBe(200);

        await Title(first).FillAsync($"Saved by the first {Mark}");
        (await SaveAsync(first, () => first.Button("Content.Form.Save").ClickAsync(), "PUT", entry)).Status.ShouldBe(412);
        await Expect(Shown(first, ConflictTitle)).ToBeVisibleAsync();
        await Expect(Dialog(first, ConflictTitle).GetByText($"Saved by the second {Mark}", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();

        var kept = await SaveAsync(first, () => DialogButton(first, ConflictTitle, "Content.Form.Conflict.KeepMine").ClickAsync(), "PUT", entry);
        kept.Status.ShouldBe(200);
        (await kept.Request.HeaderValueAsync("if-match")).ShouldBe("\"2\"");
        await Expect(first.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = $"Saved by the first {Mark}", Exact = true })).ToBeVisibleAsync();
        await Expect(Shown(first, ConflictTitle)).ToBeHiddenAsync();

        // Now the second tab is stale: take theirs shows what the first saved, with nothing left to save.
        await Title(second).FillAsync($"Saved by the second again {Mark}");
        (await SaveAsync(second, () => second.Button("Content.Form.Save").ClickAsync(), "PUT", entry)).Status.ShouldBe(412);
        await DialogButton(second, ConflictTitle, "Content.Form.Conflict.TakeTheirs").ClickAsync();
        await Expect(Title(second)).ToHaveValueAsync($"Saved by the first {Mark}");
        await Expect(Shown(second, ConflictTitle)).ToBeHiddenAsync();
        await Home(second).ClickAsync();
        await Expect(second.Page).ToHaveURLAsync(new Uri(Host.Admin, "").AbsoluteUri, UrlWait);
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
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, editor);
        await Expect(Title(tab)).ToBeVisibleAsync();

        // The form's address names its content locale (A-17).
        var form = new Uri(Host.Admin, "content/article/new?locale=en").AbsoluteUri;
        await Expect(tab.Page).ToHaveURLAsync(form, UrlWait);

        // Nothing changed: the route changes at once.
        await Home(tab).ClickAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, "").AbsoluteUri, UrlWait);
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
        await Ui.UntilAsync(() => unloadQuestions == 1, "the browser did not ask before the tab closed");
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

    // Back asks too, and a confirmed leave goes back as Back does, adding no entry to the history.
    [Fact]
    public async Task BackWithUnsavedChangesAsksAndALeaveGoesBackWithoutANewHistoryEntry()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "");
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        var start = tab.Page.Url;
        await tab.Page.EvaluateAsync("() => Blazor.navigateTo('content/article/new')");
        await Title(tab).ClickAsync();
        await Title(tab).PressSequentiallyAsync(" changed");
        var form = tab.Page.Url;
        var entries = await tab.Page.EvaluateAsync<int>("() => history.length");

        await tab.Page.EvaluateAsync("() => history.back()");
        await Expect(Shown(tab, LeaveTitle)).ToBeVisibleAsync();
        await DialogButton(tab, LeaveTitle, "Content.Form.Leave.Cancel").ClickAsync();
        await Expect(Shown(tab, LeaveTitle)).ToBeHiddenAsync();
        await Expect(tab.Page).ToHaveURLAsync(form, UrlWait);
        await Expect(Title(tab)).ToHaveValueAsync(new Regex(" changed$"));

        await tab.Page.EvaluateAsync("() => history.back()");
        await DialogButton(tab, LeaveTitle, "Content.Form.Leave.Confirm").ClickAsync();

        await Expect(tab.Page).ToHaveURLAsync(start, UrlWait);
        (await tab.Page.EvaluateAsync<int>("() => history.length")).ShouldBe(entries);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // Keep working leaves the switcher showing the site of the form, the switch then asks again, and confirmed it switches.
    [Fact]
    public async Task ASiteSwitchWithUnsavedChangesAsksAndKeepWorkingShowsTheSiteBefore()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "content/article/new");
        await Expect(Title(tab)).ToBeVisibleAsync();
        var site = await tab.SiteSwitcher.InputValueAsync();
        var other = Host.Sites.First(candidate => !string.Equals(candidate.Id, site, StringComparison.OrdinalIgnoreCase)).Id;
        await Title(tab).ClickAsync();
        await Title(tab).PressSequentiallyAsync(" changed");

        await Ui.FocusAsync(tab.SiteSwitcher);
        await tab.SiteSwitcher.SelectOptionAsync(other);
        await Expect(Shown(tab, SwitchTitle)).ToBeVisibleAsync();
        await DialogButton(tab, SwitchTitle, "Shell.SiteSwitcher.UnsavedCancel").ClickAsync();

        await Expect(Shown(tab, SwitchTitle)).ToBeHiddenAsync();
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(site);
        await Expect(tab.SiteSwitcher).ToBeFocusedAsync();
        await Expect(Title(tab)).ToHaveValueAsync(new Regex(" changed$"));

        await tab.SiteSwitcher.SelectOptionAsync(other);
        await Expect(Shown(tab, SwitchTitle)).ToBeVisibleAsync();
        await DialogButton(tab, SwitchTitle, "Shell.SiteSwitcher.UnsavedConfirm").ClickAsync();

        await Expect(tab.SiteSwitcher).ToHaveValueAsync(other);
        await Expect(Shown(tab, LeaveTitle)).ToBeHiddenAsync();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // Not on WebKit (spec 7, #3648): Playwright's WebKit runs the unload handlers of a close and never closes the page.
    [Fact]
    [Trait("NotOn", "webkit")]
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
        await browser.SignInOnTheHostedPageAsync(tab, await EditorAsync());
        await Title(tab).ClickAsync();
        await Title(tab).PressSequentiallyAsync($"{Mark} saved");
        await SaveAsync(tab, () => tab.Button("Content.Form.Save").ClickAsync(), "POST", tab.ManagementUrl("content/article"));
        await Expect(tab.Page).ToHaveURLAsync(new Regex("/admin/content/article/[0-9a-f-]{36}\\?locale=en$"), UrlWait);

        await tab.Page.CloseAsync(new PageCloseOptions { RunBeforeUnload = true });

        await Ui.UntilAsync(() => tab.Page.IsClosed, "the tab did not close");
        unloadQuestions.ShouldBe(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static ILocator Title(AdminTab tab) => tab.Page.Locator("[data-field='title'] input");

    private static ILocator Home(AdminTab tab) => tab.Page.Locator("a.shell-home");

    private static ILocator SignOut(AdminTab tab) =>
        tab.Page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = Repository.AdminText("Shell.UserMenu.SignOut"), Exact = true });

    // The dialog as the accessibility tree has it: its host element has no box of its own, the library shows it in the top layer.
    private static ILocator Shown(AdminTab tab, string titleKey) => tab.ShownDialog(Repository.AdminText(titleKey));

    // The library's dialog is named by the title the wrapper sets on its host element, whose buttons are its light children.
    private static ILocator Dialog(AdminTab tab, string titleKey) => tab.Dialog(Repository.AdminText(titleKey));

    private static ILocator DialogButton(AdminTab tab, string titleKey, string buttonKey) => Ui.ButtonIn(Dialog(tab, titleKey), Repository.AdminText(buttonKey));

    private static Task<IResponse> SaveAsync(AdminTab tab, Func<Task> action, string method, string url) => tab.AnswerAsync(action, method, url);
}
