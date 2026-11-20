using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The bulk publish of A-30's acceptance for the built-in Editor on the Blog sample: 200 articles of the case's own mark,
// selected on two pages of 100, published in four chunks of 50 each with its own key, with the progress and the result of
// each; the one article that cannot be published shows its problem, and the list keeps it selected after the run.
public sealed class BulkTests(EndToEndHost host) : BrowserTest(host)
{
    private const int Count = 200;
    private const int Failing = 42;

    [Fact]
    public async Task TheEditorPublishesTwoHundredSelectedArticlesWithProgressAndTheResultOfEach()
    {
        var mark = $"bulk{Guid.NewGuid():N}"[..14];
        using var api = await Host.ManagementClientAsync();
        await CreateArticlesAsync(api, mark);

        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        List<(string? Key, IResponse Response)> chunks = [];
        tab.Page.Response += (_, response) =>
        {
            if (response.Request.Method == "POST" && response.Url.EndsWith("/content/article/bulk", StringComparison.Ordinal))
            {
                lock (chunks)
                {
                    chunks.Add((response.Request.Headers.GetValueOrDefault("idempotency-key"), response));
                }
            }
        };

        await tab.Page.GotoAsync(new Uri(Host.Admin, $"content/article?filter[title][startsWith]={mark}&sort=title&pageSize=100").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        var grid = tab.Page.GetByRole(AriaRole.Grid, new PageGetByRoleOptions { Name = "Article", Exact = true });
        var selection = tab.Page.Locator(".content-list-selection");

        // The selection is kept across the two pages.
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(100, new LocatorAssertionsToHaveCountOptions { Timeout = Wait });
        await Expect(grid.Locator("tbody tr").First).ToContainTextAsync(Title(mark, 1));
        await grid.Locator("thead button[role=checkbox]").ClickAsync();
        await Expect(selection).ToContainTextAsync(Text("Content.List.PageSelected", "100"));
        await tab.Button("Content.List.Next").ClickAsync();
        await Expect(grid.Locator("tbody tr").First).ToContainTextAsync(Title(mark, 101));
        await grid.Locator("thead button[role=checkbox]").ClickAsync();
        await Expect(selection).ToContainTextAsync(Text("Content.List.Selected", "200"));

        await selection.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = Text("Content.Bulk.Menu"), Exact = true }).ClickAsync();
        await tab.Page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = Text("Content.Bulk.Publish"), Exact = true }).ClickAsync();
        var confirmation = tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = Text("Content.Bulk.Publish"), Exact = true });
        await Expect(confirmation).ToBeVisibleAsync();
        await Expect(DialogHost(tab, Text("Content.Bulk.Publish"))).ToContainTextAsync(Text("Content.Bulk.Confirm.Publish", "200", "Article"));
        await DialogButton(tab, Text("Content.Bulk.Publish"), Text("Content.Bulk.Publish")).ClickAsync();

        var progressTitle = Text("Content.Bulk.Title", Text("Content.Bulk.Publish"), "Article");
        var shown = tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = progressTitle, Exact = true });
        await Expect(shown).ToBeVisibleAsync();
        var progress = DialogHost(tab, progressTitle);
        await Expect(progress.Locator(".bulk-progress-summary")).ToHaveTextAsync(Text("Content.Bulk.Ended", "199", "1"), new LocatorAssertionsToHaveTextOptions { Timeout = Wait });
        await Expect(progress.Locator("progress")).ToHaveAttributeAsync("value", "200");
        await Expect(tab.Announced(Text("Content.Bulk.Progress", "150", "200", "1"))).ToBeAttachedAsync();

        lock (chunks)
        {
            chunks.Count.ShouldBe(4);
            chunks.Select(chunk => chunk.Response.Status).ShouldAllBe(status => status == 200);
            chunks.Select(chunk => chunk.Key).ShouldAllBe(key => !string.IsNullOrEmpty(key));
            chunks.Select(chunk => chunk.Key).Distinct().Count().ShouldBe(4);
        }

        var failed = progress.Locator(".bulk-progress-results tbody tr").Filter(new LocatorFilterOptions { HasText = Title(mark, Failing) });
        await Expect(failed).ToContainTextAsync(Text("Content.Bulk.Status.failed"));
        await Expect(failed.Locator("td").Nth(2)).Not.ToBeEmptyAsync();
        await Expect(progress.Locator(".bulk-progress-results tbody tr").Filter(new LocatorFilterOptions { HasText = Title(mark, Failing + 1) })).ToContainTextAsync(Text("Content.Bulk.Status.published"));

        await DialogButton(tab, progressTitle, Text("Content.Bulk.Close")).ClickAsync();
        await Expect(shown).ToBeHiddenAsync();
        await Expect(selection).ToContainTextAsync(Text("Content.List.Selected", "1"));

        using var published = await api.GetAsync(new Uri($"api/management/v1/content/article?status=published&filter[title][startsWith]={mark}&pageSize=1&withTotal=true", UriKind.Relative), TestContext.Current.CancellationToken);
        published.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await published.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken))!["total"]!.GetValue<long>().ShouldBe(Count - 1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // One bulk create of the case's articles; the failing one is a video without its URL, which a draft may lack and a
    // publish may not.
    private static async Task CreateArticlesAsync(HttpClient api, string mark)
    {
        var entries = new JsonArray([.. Enumerable.Range(1, Count).Select(n => (JsonNode)new JsonObject
        {
            ["title"] = Title(mark, n),
            ["slug"] = $"{mark}-{n:D3}",
            ["category"] = n == Failing ? "video" : "news",
        })]);
        using var created = await api.PostAsJsonAsync(new Uri("api/management/v1/content/article/bulk", UriKind.Relative), new JsonObject { ["operation"] = "create", ["entries"] = entries }, TestContext.Current.CancellationToken);
        var answer = await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, answer);
        JsonNode.Parse(answer)!["items"]!.AsArray().Select(item => item!["status"]!.GetValue<string>()).ShouldAllBe(status => status == "succeeded", answer);
    }

    private static string Title(string mark, int n) => $"{mark} {n.ToString("D3", CultureInfo.InvariantCulture)}";

    private static float Wait => (float)AdminBrowser.Wait.TotalMilliseconds;

    // The library's dialog shows its light children through slots, so its content is found through its host element.
    private static ILocator DialogHost(AdminTab tab, string title) => tab.Page.Locator($"fluent-dialog[aria-label=\"{title}\"]");

    private static ILocator DialogButton(AdminTab tab, string title, string button) =>
        DialogHost(tab, title).Locator("fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new System.Text.RegularExpressions.Regex($@"^\s*{System.Text.RegularExpressions.Regex.Escape(button)}\s*$") });

    private static string Text(string key, params string[] arguments)
    {
        var text = Repository.AdminText(key);
        for (var index = 0; index < arguments.Length; index++)
        {
            text = text.Replace($"{{{index}}}", arguments[index], StringComparison.Ordinal);
        }

        return text;
    }
}
