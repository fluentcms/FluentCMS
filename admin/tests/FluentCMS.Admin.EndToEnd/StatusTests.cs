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
        var tab = await SignedInTabAsync(browser, "content/article/new");
        await Title(tab).FillAsync($"Published from the form {Mark}");

        // The new entry is created first, then published on the tag of its create.
        var publishing = tab.NextAnswerAsync(response => response.Request.Method == "POST" && response.Url.EndsWith("/publish", StringComparison.Ordinal));
        var id = await Ui.CreatedIdAsync(await tab.AnswerAsync(() => Control(tab, "Content.Form.Publish").ClickAsync(), "POST", tab.ManagementUrl("content/article")));
        var entry = tab.ManagementUrl($"content/article/{id:D}");
        var published = await publishing;
        published.Url.ShouldBe($"{entry}/publish");
        published.Status.ShouldBe(200);
        (await published.Request.HeaderValueAsync("if-match")).ShouldBe("\"1\"");
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}\\?locale=en$"), new PageAssertionsToHaveURLOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("published")]);
        await Expect(tab.Announced(Repository.AdminText("Content.Form.Published"))).ToBeAttachedAsync();
        await Expect(Control(tab, "Content.Form.Publish")).ToHaveCountAsync(0);

        await Title(tab).FillAsync($"Changed after the publish {Mark}");
        (await tab.AnswerAsync(() => Control(tab, "Content.Form.Save").ClickAsync(), "PUT", entry)).Status.ShouldBe(200);
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("modified")]);

        await Control(tab, "Content.Form.Discard").ClickAsync();
        await Expect(Shown(tab, DiscardTitle)).ToBeVisibleAsync();
        await Expect(DialogButton(tab, DiscardTitle, "Content.Form.Discard.Cancel")).ToBeFocusedAsync();
        var discarded = await tab.AnswerAsync(() => DialogButton(tab, DiscardTitle, "Content.Form.Discard").ClickAsync(), "POST", $"{entry}/discard");
        discarded.Status.ShouldBe(200);
        (await discarded.Request.HeaderValueAsync("if-match")).ShouldBe("\"2\"");
        await Expect(Title(tab)).ToHaveValueAsync($"Published from the form {Mark}");
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("published")]);

        await Control(tab, "Content.Form.Unpublish").ClickAsync();
        await Expect(Shown(tab, UnpublishTitle)).ToBeVisibleAsync();
        (await tab.AnswerAsync(() => DialogButton(tab, UnpublishTitle, "Content.Form.Unpublish").ClickAsync(), "POST", $"{entry}/unpublish")).Status.ShouldBe(200);
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("unpublished")]);
        await Expect(Control(tab, "Content.Form.Publish")).ToBeVisibleAsync();
        await Expect(Title(tab)).ToHaveValueAsync($"Published from the form {Mark}");
        browser.PolicyViolations.ShouldBeEmpty();
    }


    private static ILocator Title(AdminTab tab) => tab.Page.Locator("[data-field='title'] input");

    // The English badge; the form shows one per locale of the site, French without a translation here (A-17).
    private static ILocator Badges(AdminTab tab) => tab.Page.Locator(".content-form-toolbar .content-badge").Filter(new LocatorFilterOptions { HasNotText = "fr" });

    private static Regex Badge(string status) =>
        new($@"^\s*en\s+{Regex.Escape(Repository.AdminText($"Content.Status.{status}"))}\s*$", RegexOptions.IgnoreCase);

    // A control of the toolbar, which the confirming button of its dialog has the same text as.
    private static ILocator Control(AdminTab tab, string resourceKey) =>
        tab.Page.Locator(".content-form-toolbar fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText(resourceKey))}\s*$") });

    // The dialog as the accessibility tree has it; its buttons are found through its host element, as in FormBehaviourTests.
    private static ILocator Shown(AdminTab tab, string titleKey) => tab.ShownDialog(Repository.AdminText(titleKey));

    private static ILocator DialogButton(AdminTab tab, string titleKey, string buttonKey) =>
        Ui.ButtonIn(tab.Dialog(Repository.AdminText(titleKey)), Repository.AdminText(buttonKey));
}
