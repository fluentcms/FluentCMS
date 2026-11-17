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
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        await Expect(tab.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = Text("Content.Form.NewHeading", "Article"), Exact = true })).ToBeVisibleAsync();

        var title = tab.Page.Locator("[data-field='title'] input");
        await title.FillAsync("Written in the browser");
        var create = await SaveAsync(tab, "POST", tab.ManagementUrl("content/article"));

        create.Status.ShouldBe(201);
        (await create.Request.HeaderValueAsync("idempotency-key")).ShouldNotBeNullOrEmpty();
        // The answers are read by their headers and by the page: the admin reads a body as a stream, and Chromium does not
        // always keep such a body for the test to read again.
        var location = await create.HeaderValueAsync("location");
        var id = Guid.Parse(location.ShouldNotBeNull()[(location.LastIndexOf('/') + 1)..]);
        await tab.Page.WaitForURLAsync(new Regex($"/admin/content/article/{id:D}$"));
        await Expect(tab.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Written in the browser", Exact = true })).ToBeVisibleAsync();
        await Expect(tab.Announced(Repository.AdminText("Content.Form.Saved"))).ToBeAttachedAsync();
        (await tab.Page.EvaluateAsync<string>("() => document.activeElement?.closest('fluent-button')?.textContent?.trim() ?? ''")).ShouldBe(Repository.AdminText("Content.Form.Save"));

        await title.FillAsync("Changed in the browser");
        var save = await SaveAsync(tab, "PUT", tab.ManagementUrl($"content/article/{id:D}"));

        save.Status.ShouldBe(200);
        (await save.Request.HeaderValueAsync("if-match")).ShouldBe("\"1\"");
        (await save.HeaderValueAsync("etag")).ShouldBe("\"2\"");
        await Expect(tab.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Changed in the browser", Exact = true })).ToBeVisibleAsync();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A-14: the article's fields have their editors, and only those of the later issues show JSON; the slug follows the title,
    // and a date and time typed in the browser's format and time zone is sent as the instant in UTC.
    [Fact]
    public async Task TheEditorWritesAnArticleWithTheCoreFieldEditors()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        await Expect(tab.Page.Locator("[data-field='title'] input")).ToBeVisibleAsync();

        var withJson = await tab.Page.Locator("[data-field]:has(.content-json)").EvaluateAllAsync<string[]>("fields => fields.map(field => field.dataset.field)");
        withJson.ShouldBeSubsetOf(["body", "author", "tags", "hero", "sections", "seo"]);

        await tab.Page.Locator("[data-field='title'] input").FillAsync("Crème brûlée, again!");
        await Expect(tab.Page.Locator("[data-field='slug'] input")).ToHaveValueAsync("creme-brulee-again");
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
        sent["slug"]!.GetValue<string>().ShouldBe("creme-brulee-again");
        sent["category"]!.GetValue<string>().ShouldBe("video");
        sent["videoUrl"]!.GetValue<string>().ShouldBe("https://example.com/watch");
        var utc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 15, 9, 30, 0, DateTimeKind.Unspecified), zone);
        sent["publishedOn"]!.GetValue<string>().ShouldBe(utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key, string argument) => Repository.AdminText(key).Replace("{0}", argument, StringComparison.Ordinal);

    private static Task<IResponse> SaveAsync(AdminTab tab, string method, string url) =>
        tab.Page.RunAndWaitForResponseAsync(
            () => tab.Button("Content.Form.Save").ClickAsync(),
            response => response.Request.Method == method && response.Url == url,
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
}
