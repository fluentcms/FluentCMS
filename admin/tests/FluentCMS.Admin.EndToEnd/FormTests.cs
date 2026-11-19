using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The edit form of A-13 for the built-in Editor on the Blog sample: a new article is created by its first save, the form
// moves to its route, and the next save sends the version it read as If-Match. Focus stays on the save button throughout.
public sealed class FormTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task TheEditorCreatesAnArticleLandsOnItsRouteChangesItAndSavesWithIfMatch()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "content/article/new");
        await Expect(tab.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = Text("Content.Form.NewHeading", "Article"), Exact = true })).ToBeVisibleAsync();

        var title = tab.Page.Locator("[data-field='title'] input");
        await title.FillAsync($"Written in the browser {Mark}");
        var create = await SaveAsync(tab, "POST", tab.ManagementUrl("content/article"));

        (await create.Request.HeaderValueAsync("idempotency-key")).ShouldNotBeNullOrEmpty();
        var id = await Ui.CreatedIdAsync(create);
        // The admin moves to the entry's route within the page. The address is polled: a wait for a navigation can start after
        // Playwright has taken that move in and then waits for one that does not come.
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}\\?locale=en$"), new PageAssertionsToHaveURLOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        await Expect(tab.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = $"Written in the browser {Mark}", Exact = true })).ToBeVisibleAsync();
        await Expect(tab.Announced(Repository.AdminText("Content.Form.Saved"))).ToBeAttachedAsync();
        await Ui.FocusReturnedAsync(tab.Button("Content.Form.Save"));

        await title.FillAsync($"Changed in the browser {Mark}");
        var save = await SaveAsync(tab, "PUT", tab.ManagementUrl($"content/article/{id:D}"));

        save.Status.ShouldBe(200);
        (await save.Request.HeaderValueAsync("if-match")).ShouldBe("\"1\"");
        (await save.HeaderValueAsync("etag")).ShouldBe("\"2\"");
        await Expect(tab.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = $"Changed in the browser {Mark}", Exact = true })).ToBeVisibleAsync();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A-14: the article's fields have their editors, and only those of the later issues show JSON; the slug follows the title,
    // and a date and time typed in the browser's format and time zone is sent as the instant in UTC.
    [Fact]
    public async Task TheEditorWritesAnArticleWithTheCoreFieldEditors()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "content/article/new");
        await Expect(tab.Page.Locator("[data-field='title'] input")).ToBeVisibleAsync();

        var withJson = await tab.Page.Locator("[data-field]:has(.content-json)").EvaluateAllAsync<string[]>("fields => fields.map(field => field.dataset.field)");
        withJson.ShouldBeSubsetOf(["body", "author", "tags", "hero", "sections", "seo"]);

        await tab.Page.Locator("[data-field='title'] input").FillAsync($"Crème brûlée, again! {Mark}");
        await Expect(tab.Page.Locator("[data-field='slug'] input")).ToHaveValueAsync($"creme-brulee-again-{Mark}");
        await tab.Page.Locator("[data-field='category'] select").SelectOptionAsync("video");
        await tab.Page.Locator("[data-field='videoUrl'] input").FillAsync("https://example.com/watch");

        var language = await tab.Page.EvaluateAsync<string>("() => navigator.language");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await tab.Page.EvaluateAsync<string>("() => Intl.DateTimeFormat().resolvedOptions().timeZone"));
        var culture = CultureInfo.GetCultureInfo(language);
        var moment = tab.Page.Locator("[data-field='publishedOn'] input");
        await moment.Nth(0).FillAsync(new DateOnly(2026, 11, 15).ToString("d", culture));
        await moment.Nth(0).PressAsync("Tab");
        await moment.Nth(1).FillAsync(new TimeOnly(9, 30).ToString(culture.DateTimeFormat.ShortTimePattern, culture));
        await moment.Nth(1).PressAsync("Tab");
        await Expect(moment.Nth(0)).Not.ToHaveAttributeAsync("aria-invalid", "true");

        var create = await SaveAsync(tab, "POST", tab.ManagementUrl("content/article"));

        create.Status.ShouldBe(201);
        var sent = JsonNode.Parse(create.Request.PostData!)!;
        sent["slug"]!.GetValue<string>().ShouldBe($"creme-brulee-again-{Mark}");
        sent["category"]!.GetValue<string>().ShouldBe("video");
        sent["videoUrl"]!.GetValue<string>().ShouldBe("https://example.com/watch");
        var utc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 15, 9, 30, 0, DateTimeKind.Unspecified), zone);
        sent["publishedOn"]!.GetValue<string>().ShouldBe(utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key, string argument) => Repository.AdminText(key).Replace("{0}", argument, StringComparison.Ordinal);

    private static Task<IResponse> SaveAsync(AdminTab tab, string method, string url) => tab.AnswerAsync(() => tab.Button("Content.Form.Save").ClickAsync(), method, url);
}
