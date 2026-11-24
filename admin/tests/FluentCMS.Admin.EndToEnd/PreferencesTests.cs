using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.PseudoLocalization;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The interface language and the time zone of a user (spec 4.17), on a publish with the pseudo-cultures, for a browser in a
// language the admin does not offer, which starts in the run's default culture, ar-XB. The user saves English and a time
// zone on the account page; the app reloads once, starts in English from its first paint, and the schedule names the zone.
// A new device starts in ar-XB, reads the preferences after sign-in and reloads once in English. The image ships English
// alone, so the run on the image leaves this case out by its trait.
[Trait("Host", "Publish")]
public sealed class PreferencesTests(EndToEndHost host) : BrowserTest(host)
{
    private const string Unoffered = "de-DE";
    private const string Zone = "Asia/Kathmandu";

    // The browser's languages, and every language the app gives the document in this load, from the first.
    private const string StartScript = $$"""
        Object.defineProperty(Navigator.prototype, 'languages', { get: () => ['{{Unoffered}}'] });
        Object.defineProperty(Navigator.prototype, 'language', { get: () => '{{Unoffered}}' });
        window.__languages = [];
        new MutationObserver(records => {
            for (const record of records) {
                if (record.target === document.documentElement) window.__languages.push(document.documentElement.lang);
            }
        }).observe(document, { attributes: true, attributeFilter: ['lang'], subtree: true });
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheLanguageAndTheTimeZoneSavedOnTheAccountPageHoldFromTheFirstPaintAndOnANewDevice()
    {
        Assert.SkipUnless(Repository.PseudoCultures, "The host is published without the pseudo-cultures: build with -p:FluentCmsAdminPseudoCultures=true.");
        Assert.SkipUnless(EndToEndHost.Culture == PseudoCulture.RightToLeft, $"The run's culture is {EndToEndHost.Culture}, not {PseudoCulture.RightToLeft}.");
        var editor = await EditorAsync();
        var article = await CreateArticleAsync();
        var browser = await OpenBrowserAsync();
        var tab = await SignedInAsync(browser, editor);
        await Expect(Document(tab)).ToHaveAttributeAsync("lang", PseudoCulture.RightToLeft);
        (await LanguagesAsync(tab)).ShouldContain(PseudoCulture.RightToLeft);

        await Field(tab, "Account.Preferences.Language", PseudoCulture.RightToLeft).SelectOptionAsync("en");
        await Field(tab, "Account.Preferences.TimeZone", PseudoCulture.RightToLeft).SelectOptionAsync(Zone);
        var loads = Loads(tab);
        var save = tab.Page.Locator("fluent-button").Filter(Ui.Exactly(PseudoCulture.Transform(Repository.AdminText("Account.Preferences.Save"), PseudoCulture.RightToLeft)));
        var saved = await tab.AnswerAsync(() => Ui.PressAsync(save, "Enter"), "PATCH", tab.ManagementUrl("me/preferences"));

        saved.Status.ShouldBe(200);
        JsonNode.Parse(saved.Request.PostData!)!.ToJsonString().ShouldBe(new JsonObject { ["culture"] = "en", ["timeZone"] = Zone }.ToJsonString());
        await Expect(Document(tab)).ToHaveAttributeAsync("lang", "en");
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        await Expect(Field(tab, "Account.Preferences.TimeZone")).ToHaveValueAsync(Zone);
        loads.ShouldHaveSingleItem().ShouldEndWith("/account");
        (await LanguagesAsync(tab)).ShouldNotContain(PseudoCulture.RightToLeft);

        await tab.Page.GotoAsync(new Uri(Host.Admin, $"content/article/{article:D}").AbsoluteUri);
        await Ui.PressAsync(tab.Page.Locator(".content-schedule fluent-button").Filter(Ui.Exactly(Repository.AdminText("Content.Schedule.Open"))), "Enter");
        await Expect(tab.Dialog(Repository.AdminText("Content.Schedule.Title")).Locator(".content-schedule-zone"))
            .ToHaveTextAsync(Ui.Format(Repository.AdminText("Content.Field.TimeZone"), Zone));

        // The sign-in is sent and its callback is still to load; the loads from here are the callback's and the reload's.
        var device = await OpenBrowserAsync();
        var other = await SignedInAsync(device, editor);
        var otherLoads = Loads(other);
        await Expect(Document(other)).ToHaveAttributeAsync("lang", "en");
        await Expect(other.Sidebar).ToBeVisibleAsync();
        await Expect(Field(other, "Account.Preferences.TimeZone")).ToHaveValueAsync(Zone);
        (await other.Page.EvaluateAsync<string?>("() => localStorage.getItem('fluentcms-admin/culture')")).ShouldBe("en");
        otherLoads.Count(path => path.EndsWith("/account", StringComparison.Ordinal)).ShouldBe(1);
        browser.PolicyViolations.ShouldBeEmpty();
        device.PolicyViolations.ShouldBeEmpty();
    }

    private static ILocator Document(AdminTab tab) => tab.Page.Locator("html");

    // A field of the preferences card by its label, in the culture the app runs in.
    private static ILocator Field(AdminTab tab, string key, string? culture = null)
    {
        var label = Repository.AdminText(key);
        return tab.Page.GetByLabel(culture is null ? label : PseudoCulture.Transform(label, culture), new PageGetByLabelOptions { Exact = true });
    }

    // The addresses of the loads of the tab from now on.
    private static ConcurrentQueue<string> Loads(AdminTab tab)
    {
        var loads = new ConcurrentQueue<string>();
        tab.Page.Load += (_, page) => loads.Enqueue(new Uri(page.Url).AbsolutePath);
        return loads;
    }

    private static Task<string[]> LanguagesAsync(AdminTab tab) => tab.Page.EvaluateAsync<string[]>("() => window.__languages");

    // A new tab of the browser, in a language the admin does not offer, at the account page, signed in on the hosted page.
    private async Task<AdminTab> SignedInAsync(AdminBrowser browser, Editor editor)
    {
        var tab = await browser.NewTabAsync();
        await tab.Page.AddInitScriptAsync(StartScript);
        await tab.Page.GotoAsync(new Uri(Host.Admin, "account").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, editor);
        return tab;
    }

    private async Task<Guid> CreateArticleAsync()
    {
        using var api = await Host.ManagementClientAsync();
        using var created = await api.PostAsJsonAsync(new Uri("api/management/v1/content/article", UriKind.Relative), new JsonObject { ["title"] = Mark }, Token);
        var answer = await created.Content.ReadAsStringAsync(Token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, answer);
        return JsonNode.Parse(answer)!["id"]!.GetValue<Guid>();
    }
}
