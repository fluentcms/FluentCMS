using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The schedules of A-27 for the built-in Editor on the Blog sample, by keyboard alone: a publish of the English locale
// scheduled years ahead in the browser's time zone, which the dialog names, seen on the form and in the list, moved and
// cancelled. Nothing waits for it to run.
public sealed class ScheduleTests(EndToEndHost host) : BrowserTest(host)
{
    private const string NewTitle = "Content.Schedule.Title";
    private const string MoveTitle = "Content.Schedule.MoveTitle.publish";

    [Fact]
    public async Task TheEditorSchedulesAPublishSeesItInTheFormAndTheListMovesItAndCancelsIt()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        var mark = $"Scheduled {Guid.NewGuid():N}";
        await tab.Page.Locator("[data-field='title'] input").FillAsync(mark);
        var created = await AnswerAsync(tab, () => tab.Page.Keyboard.PressAsync("Control+s"), "POST", tab.ManagementUrl("content/article"));
        created.Status.ShouldBe(201);
        var location = (await created.HeaderValueAsync("location")).ShouldNotBeNull();
        var entry = tab.ManagementUrl($"content/article/{location[(location.LastIndexOf('/') + 1)..]}");

        var culture = CultureInfo.GetCultureInfo(await tab.Page.EvaluateAsync<string>("() => navigator.language"));
        var zone = await tab.Page.EvaluateAsync<string>("() => Intl.DateTimeFormat().resolvedOptions().timeZone");
        var day = new DateOnly(2099, 6, 1);

        await Control(tab, "Content.Schedule.Open").PressAsync("Enter");
        await Expect(Shown(tab, NewTitle)).ToBeVisibleAsync();
        await Expect(Content(tab, NewTitle).Locator(".content-schedule-zone")).ToHaveTextAsync(Text("Content.Field.TimeZone", zone));
        await EnterAsync(tab, NewTitle, culture, day, new TimeOnly(9, 0));
        var scheduled = await AnswerAsync(tab, () => DialogButton(tab, NewTitle, "Content.Schedule.Submit").PressAsync("Enter"), "POST", $"{entry}/schedule");
        scheduled.Status.ShouldBe(200);
        var sent = JsonNode.Parse(scheduled.Request.PostData!)!;
        (sent["action"]!.GetValue<string>(), sent["at"]!.GetValue<string>(), sent["timeZone"]!.GetValue<string>()).ShouldBe(("publish", "2099-06-01T09:00:00", zone));
        await Expect(Shown(tab, NewTitle)).ToBeHiddenAsync();
        await Expect(FormSchedule(tab)).ToHaveTextAsync(Pending(culture, day, "9:00", zone));

        await tab.Page.GotoAsync(new Uri(Host.Admin, $"content/article?filter[title][contains]={Uri.EscapeDataString(mark)}").AbsoluteUri);
        var grid = tab.Page.GetByRole(AriaRole.Grid, new PageGetByRoleOptions { Name = "Article", Exact = true });
        var row = grid.Locator("tbody tr").Filter(new LocatorFilterOptions { HasText = mark });
        await Expect(row.Locator(".content-badge-schedule")).ToHaveTextAsync(Pending(culture, day, "9:00", zone));
        await row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = mark, Exact = true }).PressAsync("Enter");
        await Expect(FormSchedule(tab)).ToHaveTextAsync(Pending(culture, day, "9:00", zone));

        // A move is the same POST again, which replaces the pending publish.
        await Control(tab, "Content.Schedule.Move.publish").PressAsync("Enter");
        await Expect(Shown(tab, MoveTitle)).ToBeVisibleAsync();
        await EnterAsync(tab, MoveTitle, culture, day, new TimeOnly(10, 0));
        var moved = await AnswerAsync(tab, () => DialogButton(tab, MoveTitle, "Content.Schedule.MoveSubmit").PressAsync("Enter"), "POST", $"{entry}/schedule");
        moved.Status.ShouldBe(200);
        JsonNode.Parse(moved.Request.PostData!)!["at"]!.GetValue<string>().ShouldBe("2099-06-01T10:00:00");
        await Expect(FormSchedule(tab)).ToHaveTextAsync(Pending(culture, day, "10:00", zone));

        var cancelled = await AnswerAsync(tab, () => Control(tab, "Content.Schedule.Cancel.publish").PressAsync("Enter"), "DELETE", $"{entry}/schedule/publish");
        cancelled.Status.ShouldBe(204);
        await Expect(FormSchedule(tab)).ToHaveCountAsync(0);
        await Expect(tab.Announced(Repository.AdminText("Content.Schedule.Cancelled.publish"))).ToBeAttachedAsync();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The date and the time in the browser's formats, each left by Tab as a user would.
    private static async Task EnterAsync(AdminTab tab, string titleKey, CultureInfo culture, DateOnly day, TimeOnly time)
    {
        var date = Content(tab, titleKey).Locator("input.cms-input-date");
        await date.FillAsync(day.ToString("d", culture));
        await date.PressAsync("Tab");
        var clock = Content(tab, titleKey).Locator("input.cms-input-time");
        await clock.FillAsync(time.ToString(culture.DateTimeFormat.ShortTimePattern, culture));
        await clock.PressAsync("Tab");
    }

    // The badge of the English publish: its date, its time and the zone it names.
    private static Regex Pending(CultureInfo culture, DateOnly day, string time, string zone) =>
        new($@"^\s*en\s+{Regex.Escape(Repository.AdminText("Content.Badge.PublishAt"))}\s+{Regex.Escape(day.ToString("d", culture))}\s+0?{Regex.Escape(time)}.*\s{Regex.Escape(zone)}\s*$");

    private static ILocator FormSchedule(AdminTab tab) => tab.Page.Locator(".content-form-toolbar .content-badge-schedule");

    private static string Text(string key, string argument) => Repository.AdminText(key).Replace("{0}", argument, StringComparison.Ordinal);

    private static ILocator Control(AdminTab tab, string resourceKey) =>
        tab.Page.Locator(".content-schedule fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText(resourceKey))}\s*$") });

    private static ILocator Shown(AdminTab tab, string titleKey) =>
        tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = Repository.AdminText(titleKey), Exact = true });

    // What the dialog holds is slotted into the library's element, outside the shadow element that has the dialog role.
    private static ILocator Content(AdminTab tab, string titleKey) =>
        tab.Page.Locator($"fluent-dialog[aria-label=\"{Repository.AdminText(titleKey)}\"]");

    private static ILocator DialogButton(AdminTab tab, string titleKey, string buttonKey) =>
        Content(tab, titleKey).Locator("fluent-button")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText(buttonKey))}\s*$") });

    private static Task<IResponse> AnswerAsync(AdminTab tab, Func<Task> action, string method, string url) =>
        tab.Page.RunAndWaitForResponseAsync(
            action,
            response => response.Request.Method == method && response.Url == url,
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
}
