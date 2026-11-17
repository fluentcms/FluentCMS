using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// Locale-aware editing (A-17) for the built-in Editor on the Blog sample: an article written in English, its French started
// as a copy of the English and added with If-Match "0", then saved, published, discarded and unpublished in French without
// touching the English, a switch back with unsaved changes after a question, and the French beside the English.
public sealed class LocaleTests(EndToEndHost host) : BrowserTest(host)
{
    private const string LeaveTitle = "Content.Form.Leave.Title";
    private const string DiscardTitle = "Content.Form.Discard.Title";
    private const string UnpublishTitle = "Content.Form.Unpublish.Title";

    [Fact]
    public async Task TheEditorPublishesInTwoLocalesAndCopiesALocale()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);

        // English: created, then published.
        await Title(tab).FillAsync("Written in two locales");
        var created = await AnswerAsync(tab, () => Control(tab, "Content.Form.Save").ClickAsync(), "POST", tab.ManagementUrl("content/article"));
        var location = (await created.HeaderValueAsync("location")).ShouldNotBeNull();
        var id = Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
        var entry = tab.ManagementUrl($"content/article/{id:D}");
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}$"), Timeout);
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("en", "draft"), Missing("fr")]);
        (await AnswerAsync(tab, () => Control(tab, "Content.Form.Publish").ClickAsync(), "POST", $"{entry}/publish")).Status.ShouldBe(200);
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("en", "published"), Missing("fr")]);

        // French: no translation yet, started as a copy of the English and added with If-Match "0".
        await Locale(tab, "fr").ClickAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}\\?locale=fr$"), Timeout);
        await Expect(tab.Page.Locator(".content-form-new-locale")).ToBeVisibleAsync();
        await Expect(Title(tab)).ToHaveValueAsync("");
        await tab.Page.Locator(".content-form-new-locale fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex(@"\ben\s*$") }).ClickAsync();
        await Expect(Title(tab)).ToHaveValueAsync("Written in two locales");
        await Expect(Title(tab)).ToHaveAttributeAsync("lang", "fr");
        await Title(tab).FillAsync("Écrit en deux langues");
        var added = await AnswerAsync(tab, () => Control(tab, "Content.Form.Save").ClickAsync(), "PUT", entry);
        added.Status.ShouldBe(200);
        (await added.Request.HeaderValueAsync("if-match")).ShouldBe("\"0\"");
        (await added.Request.HeaderValueAsync("x-cms-locale")).ShouldBe("fr");
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("en", "published"), Badge("fr", "draft")]);

        // Published, changed, the change discarded, then unpublished: each in French alone.
        (await AnswerAsync(tab, () => Control(tab, "Content.Form.Publish").ClickAsync(), "POST", $"{entry}/publish")).Status.ShouldBe(200);
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("en", "published"), Badge("fr", "published")]);
        await Title(tab).FillAsync("Changé");
        (await AnswerAsync(tab, () => Control(tab, "Content.Form.Save").ClickAsync(), "PUT", entry)).Status.ShouldBe(200);
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("en", "published"), Badge("fr", "modified")]);
        await Control(tab, "Content.Form.Discard").ClickAsync();
        await Expect(Shown(tab, DiscardTitle)).ToBeVisibleAsync();
        (await AnswerAsync(tab, () => DialogButton(tab, DiscardTitle, "Content.Form.Discard").ClickAsync(), "POST", $"{entry}/discard")).Status.ShouldBe(200);
        await Expect(Title(tab)).ToHaveValueAsync("Écrit en deux langues");
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("en", "published"), Badge("fr", "published")]);
        await Control(tab, "Content.Form.Unpublish").ClickAsync();
        await Expect(Shown(tab, UnpublishTitle)).ToBeVisibleAsync();
        (await AnswerAsync(tab, () => DialogButton(tab, UnpublishTitle, "Content.Form.Unpublish").ClickAsync(), "POST", $"{entry}/unpublish")).Status.ShouldBe(200);
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("en", "published"), Badge("fr", "unpublished")]);

        // Back to English with an unsaved change: the form asks, and a leave reads the English.
        await Title(tab).FillAsync("Not kept");
        await Locale(tab, "en").ClickAsync();
        await Expect(Shown(tab, LeaveTitle)).ToBeVisibleAsync();
        await DialogButton(tab, LeaveTitle, "Content.Form.Leave.Confirm").ClickAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}\\?locale=en$"), Timeout);
        await Expect(Title(tab)).ToHaveValueAsync("Written in two locales");
        await Expect(Badges(tab)).ToHaveTextAsync([Badge("en", "published"), Badge("fr", "unpublished")]);

        // Side by side: the French, read-only, beside the English.
        await tab.Page.GetByLabel(Repository.AdminText("Content.Form.Compare.Label"), new PageGetByLabelOptions { Exact = true }).SelectOptionAsync("fr");
        var french = tab.Page.Locator("[data-field='title'] .content-form-compare input");
        await Expect(french).ToHaveValueAsync("Écrit en deux langues");
        await Expect(french).ToHaveAttributeAsync("lang", "fr");
        await Expect(french).ToHaveAttributeAsync("readonly", "");

        // The list in French reads the French and the status of the English by ids.
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article?locale=fr").AbsoluteUri);
        var row = tab.Page.GetByRole(AriaRole.Grid).Locator("tbody tr").Filter(new LocatorFilterOptions { HasText = "Écrit en deux langues" });
        await Expect(row.Locator(".content-badge")).ToHaveTextAsync([Badge("en", "published"), Badge("fr", "unpublished")]);
        await Expect(row.Locator("a").First).ToHaveAttributeAsync("href", $"content/article/{id:D}?locale=fr");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static PageAssertionsToHaveURLOptions Timeout => new() { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds };

    private static ILocator Title(AdminTab tab) => tab.Page.Locator("[data-field='title'] input").First;

    private static ILocator Badges(AdminTab tab) => tab.Page.Locator(".content-form-toolbar .content-badge");

    private static ILocator Locale(AdminTab tab, string code) =>
        tab.Page.Locator(".content-locales a").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($"^{code}$") });

    private static Regex Badge(string locale, string status) =>
        new($@"^\s*{locale}\s+{Regex.Escape(Repository.AdminText($"Content.Status.{status}"))}\s*$", RegexOptions.IgnoreCase);

    private static Regex Missing(string locale) =>
        new($@"^\s*{locale}\s*{Regex.Escape(Repository.AdminText("Content.Badge.Missing"))}\s*$", RegexOptions.IgnoreCase);

    private static ILocator Control(AdminTab tab, string resourceKey) =>
        tab.Page.Locator(".content-form-toolbar fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText(resourceKey))}\s*$") });

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
