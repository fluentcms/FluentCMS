using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The plugins, jobs and diagnostics pages of A-39 on the reference host, by keyboard: a Super Admin turns the webhooks plugin
// off in the default site, whose menu item leaves the sidebar without a reload, and on again; cancels a pending job, which
// goes among the dead letters, and retries it from there; and reads the startup report and the health. The pending job is
// the second attempt of a webhook delivery to a host that never resolves, which the plugin schedules a minute after the first.
public sealed class OperationsTests(EndToEndHost host) : BrowserTest(host)
{
    private const string Delivery = "webhooks.deliver";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAdministratorTurnsAPluginOffInTheSiteAndItsMenuItemLeavesWithoutAReloadAndComesBack()
    {
        var site = Host.Sites[0];
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "plugins", await Host.CreateEditorAsync("Super Admin"));
        await Expect(Heading(tab, Repository.AdministrationHeading("Plugins"))).ToBeVisibleAsync();
        var entry = tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Webhooks", Exact = true });
        await Expect(entry).ToBeVisibleAsync();
        var card = Region(tab, "webhooks");
        var toggle = await SwitchAsync(tab, card, Ui.Format(Plugins("Plugins.Sites.Switch"), site.Name));
        await tab.Page.EvaluateAsync("() => window.e2eLoaded = true");
        try
        {
            var off = await tab.AnswerAsync(() => Ui.PressAsync(toggle, "Space"), "PUT", tab.ManagementUrl($"plugins/webhooks/sites/{site.Id}"));
            off.Status.ShouldBe(200);
            await Expect(card.Locator(".plugins-message")).ToHaveTextAsync(Ui.Format(Plugins("Plugins.Sites.Disabled"), "webhooks", site.Name));
            await Expect(entry).ToHaveCountAsync(0);
            await Expect(card).ToContainTextAsync(Plugins("Plugins.Entry.AdminPagesDisabled"));

            var on = await tab.AnswerAsync(() => Ui.PressAsync(toggle, "Space"), "PUT", tab.ManagementUrl($"plugins/webhooks/sites/{site.Id}"));
            on.Status.ShouldBe(200);
            await Expect(card.Locator(".plugins-message")).ToHaveTextAsync(Ui.Format(Plugins("Plugins.Sites.Enabled"), "webhooks", site.Name));
            await Expect(entry).ToBeVisibleAsync();
            await Expect(card.Locator("ul.plugins-pages a[href='webhooks']")).ToBeVisibleAsync();
            (await tab.Page.EvaluateAsync<bool>("() => window.e2eLoaded === true")).ShouldBeTrue();
        }
        finally
        {
            using var management = await Host.ManagementClientAsync();
            using var restored = await management.PutAsJsonAsync(new Uri($"api/management/v1/plugins/webhooks/sites/{site.Id}", UriKind.Relative), new { enabled = true }, CancellationToken.None);
        }

        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAdministratorCancelsAPendingJobAndRetriesItFromTheDeadLetters()
    {
        using var management = await Host.ManagementClientAsync();
        var webhook = await WebhookAsync(management);
        try
        {
            // Signed in first, so that the job's minute is spent on the page alone.
            var browser = await OpenBrowserAsync();
            var tab = await SignedInTabAsync(browser, "jobs", await Host.CreateEditorAsync("Super Admin"));
            var main = tab.Page.Locator("main");
            await Expect(Heading(tab, Repository.AdministrationHeading("Jobs"))).ToBeVisibleAsync();
            var job = await PendingDeliveryAsync(management);
            await tab.TokenRequestOfAsync(() => tab.Page.GotoAsync(new Uri(Host.Admin, $"jobs?status=pending&type={Delivery}").AbsoluteUri));
            await Expect(Heading(tab, Repository.AdministrationHeading("Jobs"))).ToBeVisibleAsync();

            // A row is one tab stop, and Enter opens the job.
            var row = main.Locator("table[role=grid] tbody tr").Filter(new LocatorFilterOptions { Has = tab.Page.Locator($"a[href='jobs/{job:D}']") });
            await Ui.PressAsync(row, "Enter");
            await Expect(Heading(tab, Delivery)).ToBeVisibleAsync();
            await Expect(Fact(tab, "Jobs.List.Status")).ToHaveTextAsync(Jobs("Jobs.Status.pending"));

            var dialog = await tab.OpenDialogAsync(() => Ui.PressAsync(Ui.ButtonIn(main, Jobs("Jobs.Job.Cancel")).First, "Enter"), Jobs("Jobs.Confirm.CancelTitle"));
            var cancelled = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(dialog, Jobs("Jobs.Confirm.CancelJob")), "Enter"), "POST", tab.ManagementUrl($"jobs/{job:D}/cancel"));
            cancelled.Status.ShouldBe(200);
            await Expect(Fact(tab, "Jobs.List.Status")).ToHaveTextAsync(Jobs("Jobs.Status.dead_lettered"));
            await Expect(main.Locator("p.jobs-message")).ToHaveTextAsync(Jobs("Jobs.Job.Cancelled"));
            await Expect(main.Locator("p.jobs-message")).ToBeFocusedAsync();

            // Back on the list, the dead letters hold it, and a retry queues it again.
            await Ui.PressAsync(main.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Jobs("Jobs.Job.BackToList"), Exact = true }), "Enter");
            var dead = Region(tab, Jobs("Jobs.DeadJobs.Heading"));
            var deadRow = dead.Locator("tbody tr").Filter(new LocatorFilterOptions { Has = tab.Page.Locator($"a[href='jobs/{job:D}']") });
            await Expect(deadRow).ToHaveCountAsync(1);
            var retried = await tab.AnswerAsync(() => Ui.PressAsync(deadRow.Locator("fluent-button"), "Enter"), "POST", tab.ManagementUrl($"jobs/{job:D}/retry"));
            retried.Status.ShouldBe(200);
            await Expect(dead.Locator("p.jobs-message")).ToHaveTextAsync(Ui.Format(Jobs("Jobs.DeadJobs.Retried"), Delivery));
            await Expect(deadRow).ToHaveCountAsync(0);

            var stored = await management.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/jobs/{job:D}", UriKind.Relative), Token);
            ((string)stored!["status"]!).ShouldNotBe("dead_lettered");
            browser.PolicyViolations.ShouldBeEmpty();
        }
        finally
        {
            using var removed = await management.DeleteAsync(new Uri($"api/management/v1/webhooks/{webhook:D}", UriKind.Relative), CancellationToken.None);
        }
    }

    [Fact]
    public async Task AnAdministratorReadsTheStartupReportWithItsInsecureDefaultsAndTheHealth()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "system/diagnostics", await Host.CreateEditorAsync("Super Admin"));
        await Expect(Heading(tab, Repository.AdministrationHeading("Diagnostics"))).ToBeVisibleAsync();

        var report = Region(tab, Diagnostics("Diagnostics.Report.Heading"));
        await Expect(report.Locator("dl").First).ToContainTextAsync("Production");
        await Expect(report.Locator("ul.diagnostics-insecure li").First).ToBeVisibleAsync();
        var health = Region(tab, Diagnostics("Diagnostics.Health.Heading"));
        await Expect(health.Locator("tbody tr").First).ToBeVisibleAsync();

        var again = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(health, Diagnostics("Diagnostics.Health.Again")), "Enter"), "GET", tab.ManagementUrl("system/health"));
        again.Status.ShouldBe(200);
        await Expect(health.Locator(".diagnostics-message")).ToHaveTextAsync(Diagnostics("Diagnostics.Health.Read"));
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A webhook of the default site for the drafts saved there, to a host under .invalid, which never resolves (RFC 6761),
    // so that every attempt fails and the next is scheduled.
    private async Task<Guid> WebhookAsync(HttpClient management)
    {
        var body = new JsonObject
        {
            ["name"] = $"Jobs {Mark}",
            ["url"] = $"https://{Mark}.invalid/hooks",
            ["events"] = new JsonArray("content.draft_saved"),
        };
        using var created = await management.PostAsJsonAsync(new Uri("api/management/v1/webhooks", UriKind.Relative), body, Token);
        var answer = await created.Content.ReadAsStringAsync(Token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, answer);
        return JsonNode.Parse(answer)!["id"]!.GetValue<Guid>();
    }

    // A draft saved after the webhook was made, whose first delivery fails, so that the plugin schedules the second a minute
    // after it. No route schedules a job for later, so the case takes only such a retry, one made after the draft whose run is
    // half a minute or more after it was made and still at least 40 s away, which leaves the page that long to cancel it; an
    // attempt that is due, as the first, may be claimed by a worker meanwhile and is never taken.
    private async Task<Guid> PendingDeliveryAsync(HttpClient management)
    {
        var saved = DateTimeOffset.UtcNow.AddSeconds(-1);
        using var draft = await management.PostAsJsonAsync(new Uri("api/management/v1/content/article", UriKind.Relative), new JsonObject { ["title"] = $"Jobs {Mark}" }, Token);
        draft.StatusCode.ShouldBe(HttpStatusCode.Created, await draft.Content.ReadAsStringAsync(Token));

        Guid? found = null;
        await Ui.UntilAsync(
            async () =>
            {
                var pending = await management.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/jobs?status=pending&type={Delivery}&pageSize=100", UriKind.Relative), Token);
                found = pending!["items"]!.AsArray()
                    .Where(item =>
                    {
                        var (created, runAt) = (item!["createdAt"]!.GetValue<DateTimeOffset>(), item["runAt"]!.GetValue<DateTimeOffset>());
                        return created >= saved && runAt - created >= TimeSpan.FromSeconds(30) && runAt - DateTimeOffset.UtcNow >= TimeSpan.FromSeconds(40);
                    })
                    .Select(item => item!["id"]!.GetValue<Guid>())
                    .Cast<Guid?>()
                    .FirstOrDefault();
                return found is not null;
            },
            "the webhook's second attempt is scheduled a minute ahead");
        return found!.Value;
    }

    private static string Plugins(string key) => Repository.AdministrationText("Plugins", key);

    private static string Jobs(string key) => Repository.AdministrationText("Jobs", key);

    private static string Diagnostics(string key) => Repository.AdministrationText("Diagnostics", key);

    private static ILocator Heading(AdminTab tab, string name) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = name, Exact = true, Level = 1 });

    // The switch of the wrapped set whose label names it, which the label's for attribute points to.
    private static async Task<ILocator> SwitchAsync(AdminTab tab, ILocator scope, string label)
    {
        var id = await scope.Locator("label").Filter(Ui.Exactly(label)).GetAttributeAsync("for");
        return tab.Page.Locator($"fluent-switch[id='{id}']");
    }

    // A card of a page, a region named by its heading.
    private static ILocator Region(AdminTab tab, string name) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Region, new LocatorGetByRoleOptions { Name = name, Exact = true });

    // The value of a term of the job's facts.
    private static ILocator Fact(AdminTab tab, string key) =>
        tab.Page.Locator("main dl.jobs-facts dt").Filter(Ui.Exactly(Jobs(key))).Locator("xpath=following-sibling::dd[1]");
}
