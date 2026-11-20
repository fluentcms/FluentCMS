using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The version history of A-28 for the built-in Editor on the Blog sample, by keyboard alone: an article saved twice has two
// versions, which are compared, the panel closes on Escape with focus back on its button, and the first version is restored
// as a new draft on the draft's tag after a question that names it.
public sealed class VersionTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task TheEditorComparesTwoVersionsAndRestoresTheFirst()
    {
        var mark = Guid.NewGuid().ToString("N")[..8];
        var (first, second) = ($"Version one {mark}", $"Version two {mark}");
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        await Title(tab).FillAsync(first);
        var created = await AnswerAsync(tab, () => Save(tab).PressAsync("Enter"), "POST", tab.ManagementUrl("content/article"));
        var location = (await created.HeaderValueAsync("location")).ShouldNotBeNull();
        var entry = tab.ManagementUrl($"content/article/{Guid.Parse(location[(location.LastIndexOf('/') + 1)..]):D}");
        await Expect(tab.Page).ToHaveURLAsync(new Regex("/admin/content/article/[0-9a-f-]{36}"), new PageAssertionsToHaveURLOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        await Title(tab).FillAsync(second);
        (await AnswerAsync(tab, () => Save(tab).PressAsync("Enter"), "PUT", entry)).Status.ShouldBe(200);

        var history = tab.Page.Locator(".content-form-history fluent-button");
        var listed = await AnswerAsync(tab, () => history.PressAsync("Enter"), "GET", $"{entry}/versions");
        listed.Status.ShouldBe(200);
        var panel = tab.Page.Locator($"fluent-drawer[aria-label=\"{Repository.AdminText("Content.Versions.Title")}\"]");
        await Expect(panel.Locator(".version-list tbody tr")).ToHaveCountAsync(2);
        await Expect(panel.Locator(".version-list tbody tr").First.Locator("th")).ToHaveTextAsync(Text("Content.Versions.Version", "2"));

        // From version 1 to version 2 is the default; Compare reads the diff and both versions.
        var compare = panel.Locator(".version-compare fluent-button");
        (await AnswerAsync(tab, () => compare.PressAsync("Enter"), "GET", $"{entry}/versions/1/diff/2")).Status.ShouldBe(200);
        var heading = panel.Locator(".version-diff h3");
        await Expect(heading).ToHaveTextAsync(Text("Content.Versions.DiffHeading", "1", "2"));
        await Expect(heading).ToBeFocusedAsync();
        var title = panel.Locator(".version-diff-table[data-field='title'] tbody tr");
        await Expect(title.Locator("td")).ToHaveTextAsync([Repository.AdminText("Content.Versions.ChangedValue"), first, second]);

        // Escape closes the panel, and focus goes back to the button that opened it.
        await tab.Page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();
        await Expect(history).ToBeFocusedAsync();
        await Expect(Title(tab)).ToHaveValueAsync(second);

        await AnswerAsync(tab, () => history.PressAsync("Enter"), "GET", $"{entry}/versions");
        var restore = panel.Locator(".version-list tbody tr").Nth(1).Locator("fluent-button");
        await restore.PressAsync("Enter");
        var askTitle = Text("Content.Versions.Ask.Title", Text("Content.Versions.Version", "1"));
        await Expect(tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = askTitle, Exact = true })).ToBeVisibleAsync();
        var confirm = tab.Page.Locator($"fluent-dialog[aria-label=\"{askTitle}\"] fluent-button")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Text("Content.Versions.Ask.Confirm", Text("Content.Versions.Version", "1")))}\s*$") });
        var restored = await AnswerAsync(tab, () => confirm.PressAsync("Enter"), "POST", $"{entry}/versions/1/restore");
        restored.Status.ShouldBe(200);
        (await restored.Request.HeaderValueAsync("if-match")).ShouldBe("\"2\"");
        await Expect(Title(tab)).ToHaveValueAsync(first);
        await Expect(tab.Announced(Text("Content.Form.RestoredVersion", "1"))).ToBeAttachedAsync();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key, params object[] arguments) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Repository.AdminText(key), arguments);

    private static ILocator Title(AdminTab tab) => tab.Page.Locator("[data-field='title'] input");

    private static ILocator Save(AdminTab tab) =>
        tab.Page.Locator(".content-form-toolbar fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText("Content.Form.Save"))}\s*$") });

    private static Task<IResponse> AnswerAsync(AdminTab tab, Func<Task> action, string method, string url) =>
        tab.Page.RunAndWaitForResponseAsync(
            action,
            response => response.Request.Method == method && response.Url == url,
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
}
