using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The status controls of A-16 for the built-in Editor on the Blog sample: an article published from the new form, changed
// until it shows Modified, its change discarded and the article unpublished, each discard and unpublish after a question.
public sealed class StatusTests(EndToEndHost host) : BrowserTest(host)
{
    private const string DiscardTitle = "Content.Form.Discard.Title";
    private const string UnpublishTitle = "Content.Form.Unpublish.Title";

    [Fact]
    public async Task TheEditorPublishesAnArticleSeesModifiedDiscardsTheChangeAndUnpublishes()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        await Title(tab).FillAsync("Published from the form");

        // The new entry is created first, then published on the tag of its create.
        var publishing = tab.Page.WaitForResponseAsync(response => response.Request.Method == "POST" && response.Url.EndsWith("/publish", StringComparison.Ordinal), Wait);
        var created = await AnswerAsync(tab, () => Control(tab, "Content.Form.Publish").ClickAsync(), "POST", tab.ManagementUrl("content/article"));
        var location = (await created.HeaderValueAsync("location")).ShouldNotBeNull();
        var id = Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
        var entry = tab.ManagementUrl($"content/article/{id:D}");
        var published = await publishing;
        published.Url.ShouldBe($"{entry}/publish");
        published.Status.ShouldBe(200);
        (await published.Request.HeaderValueAsync("if-match")).ShouldBe("\"1\"");
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}$"), new PageAssertionsToHaveURLOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("published")]);
        await Expect(tab.Announced(Repository.AdminText("Content.Form.Published"))).ToBeAttachedAsync();
        await Expect(Control(tab, "Content.Form.Publish")).ToHaveCountAsync(0);

        await Title(tab).FillAsync("Changed after the publish");
        (await AnswerAsync(tab, () => Control(tab, "Content.Form.Save").ClickAsync(), "PUT", entry)).Status.ShouldBe(200);
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("modified")]);

        await Control(tab, "Content.Form.Discard").ClickAsync();
        await Expect(Shown(tab, DiscardTitle)).ToBeVisibleAsync();
        (await FocusedButtonAsync(tab)).ShouldBe(Repository.AdminText("Content.Form.Discard.Cancel"));
        var discarded = await AnswerAsync(tab, () => DialogButton(tab, DiscardTitle, "Content.Form.Discard").ClickAsync(), "POST", $"{entry}/discard");
        discarded.Status.ShouldBe(200);
        (await discarded.Request.HeaderValueAsync("if-match")).ShouldBe("\"2\"");
        await Expect(Title(tab)).ToHaveValueAsync("Published from the form");
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("published")]);

        await Control(tab, "Content.Form.Unpublish").ClickAsync();
        await Expect(Shown(tab, UnpublishTitle)).ToBeVisibleAsync();
        (await AnswerAsync(tab, () => DialogButton(tab, UnpublishTitle, "Content.Form.Unpublish").ClickAsync(), "POST", $"{entry}/unpublish")).Status.ShouldBe(200);
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("unpublished")]);
        await Expect(Control(tab, "Content.Form.Publish")).ToBeVisibleAsync();
        await Expect(Title(tab)).ToHaveValueAsync("Published from the form");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static PageWaitForResponseOptions Wait => new() { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds };

    private static ILocator Title(AdminTab tab) => tab.Page.Locator("[data-field='title'] input");

    private static ILocator Badges(AdminTab tab) => tab.Page.Locator(".content-form-toolbar .content-badge");

    private static Regex Badge(string status) =>
        new($@"^\s*en\s+{Regex.Escape(Repository.AdminText($"Content.Status.{status}"))}\s*$", RegexOptions.IgnoreCase);

    // The library moves focus into the dialog on its own timer, so the check waits for it.
    private static async Task<string> FocusedButtonAsync(AdminTab tab)
    {
        await tab.Page.WaitForFunctionAsync("() => document.activeElement?.closest('fluent-button') != null", null, new PageWaitForFunctionOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        return await tab.Page.EvaluateAsync<string>("() => document.activeElement?.closest('fluent-button')?.textContent?.trim() ?? ''");
    }

    // A control of the toolbar, which the confirming button of its dialog has the same text as.
    private static ILocator Control(AdminTab tab, string resourceKey) =>
        tab.Page.Locator(".content-form-toolbar fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText(resourceKey))}\s*$") });

    // The dialog as the accessibility tree has it; its buttons are found through its host element, as in FormBehaviourTests.
    private static ILocator Shown(AdminTab tab, string titleKey) =>
        tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = Repository.AdminText(titleKey), Exact = true });

    private static ILocator DialogButton(AdminTab tab, string titleKey, string buttonKey) =>
        tab.Page.Locator($"fluent-dialog[aria-label=\"{Repository.AdminText(titleKey)}\"] fluent-button")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText(buttonKey))}\s*$") });

    private static Task<IResponse> AnswerAsync(AdminTab tab, Func<Task> action, string method, string url) =>
        tab.Page.RunAndWaitForResponseAsync(
            action,
            response => response.Request.Method == method && response.Url == url,
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
}
