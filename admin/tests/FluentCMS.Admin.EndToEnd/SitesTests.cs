using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The sites page of the admin and the multi-site half (spec 4.16, 5.6) on the reference host, by keyboard: an administrator
// creates a site, gives it a second host name that is made primary, adds a locale with a fallback to it, which the locale
// switcher of a content form follows as it is disabled, enabled and removed, and deletes the site while the admin shows it,
// which moves the admin to the default site. A
// caller who holds sites.manage in a site only gets the site's locales and none of the half's pages, and in a site where the
// multi-site plugin is disabled the half has no menu item, its page is not found and it is never fetched. A case that makes
// a site deletes it, so that the other classes see the two sites of the host.
public sealed class SitesTests(EndToEndHost host) : BrowserTest(host)
{
    private static readonly Regex MultiSiteHalf = new(@"/_framework/FluentCMS\.Plugins\.MultiSite\.Admin(\.[0-9a-z]+)?\.wasm\z", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> MultiSiteTexts =
        XDocument.Load(Path.Combine(Repository.Root, "admin", "src", "plugins", "FluentCMS.Plugins.MultiSite.Admin", "MultiSiteResources.resx")).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!, StringComparer.Ordinal);

    [Fact]
    public async Task AnAdministratorCreatesASiteMakesASecondHostNamePrimaryChangesItsLocalesAndDeletesTheSiteItIsIn()
    {
        var (name, slug) = ($"Sites {Mark}", Mark);
        string? id = null;
        try
        {
            var browser = await OpenBrowserAsync();
            var tab = await SignedInTabAsync(browser, "multi-site/sites", await Host.CreateEditorAsync("Super Admin"));
            var main = tab.Page.Locator("main");
            await Expect(Heading(tab, MultiSite("MultiSite.Sites.Page.Heading"))).ToBeVisibleAsync();

            var create = Region(tab, MultiSite("MultiSite.Create.Heading"));
            await Field(create, MultiSite("MultiSite.Create.Name")).FillAsync(name);
            await Field(create, MultiSite("MultiSite.Create.Slug")).FillAsync(slug);
            await Field(create, MultiSite("MultiSite.Create.DefaultLocale")).FillAsync("en");
            var created = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(create, MultiSite("MultiSite.Create.Create")), "Enter"), "POST", tab.ManagementUrl("sites"));
            id = (await Ui.CreatedIdAsync(created, "sites")).ToString("D");
            await Expect(tab.SiteSwitcher.Locator("option", new LocatorLocatorOptions { HasText = name })).ToHaveCountAsync(1);

            // Two host names; the first is the primary host until the second is made primary.
            await Ui.PressAsync(main.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = MultiSite("MultiSite.Sites.Hosts") + Ui.Format(MultiSite("MultiSite.Sites.OfWhich"), name), Exact = true }), "Enter");
            await Expect(Heading(tab, Ui.Format(MultiSite("MultiSite.Hosts.HeadingOf"), name))).ToBeVisibleAsync();
            var (first, second) = ($"a-{Mark}.example.test", $"b-{Mark}.example.test");
            foreach (var hostname in (string[])[first, second])
            {
                var add = Region(tab, MultiSite("MultiSite.Hosts.AddHeading"));
                await Field(add, MultiSite("MultiSite.Hosts.Hostname")).FillAsync(hostname);
                var added = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(add, MultiSite("MultiSite.Hosts.Add")), "Enter"), "POST", tab.ManagementUrl($"sites/{id}/hosts"));
                added.Status.ShouldBe(201);
            }

            var hosts = main.Locator("ul.multi-site-hosts li");
            await Expect(hosts).ToHaveCountAsync(2);
            var primary = await tab.AnswerAsync(
                () => Ui.PressAsync(Ui.ButtonIn(main, MultiSite("MultiSite.Hosts.MakePrimary") + Ui.Format(MultiSite("MultiSite.Hosts.Which"), second)), "Enter"),
                response => response.Request.Method == "PATCH" && response.Url.StartsWith(tab.ManagementUrl($"sites/{id}/hosts/"), StringComparison.Ordinal));
            primary.Status.ShouldBe(200);
            await Expect(hosts.First).ToContainTextAsync(second);
            await Expect(hosts.First).ToContainTextAsync(MultiSite("MultiSite.Hosts.Primary"));

            // In the new site, a locale with a fallback, which the locale switcher of a content form offers at once, and not
            // once it is disabled, again once it is enabled, and not once it is removed; no step reloads the page.
            await tab.SwitchSiteAsync(new Site(id, name, false));
            await OpenSitesAsync(tab);
            var locale = Region(tab, Sites("Sites.Add.Heading"));
            await Field(locale, Sites("Sites.Add.Code")).FillAsync("de");
            await Field(locale, Sites("Sites.Add.Fallbacks")).FillAsync("en");
            var localeAdded = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(locale, Sites("Sites.Add.Add")), "Enter"), "POST", tab.ManagementUrl($"sites/{id}/locales"));
            localeAdded.Status.ShouldBe(201);
            await Expect(main.Locator("table.sites-locales tbody th").Filter(new LocatorFilterOptions { HasTextRegex = new Regex("^de$") })).ToHaveCountAsync(1);
            await Expect(await FormLocaleAsync(tab, "de")).ToHaveCountAsync(1);

            foreach (var (action, method, shown) in new (string Action, string Method, int Shown)[] { ("Sites.Locales.Disable", "PATCH", 0), ("Sites.Locales.Enable", "PATCH", 1), ("Sites.Locales.Remove", "DELETE", 0) })
            {
                await OpenSitesAsync(tab);
                var locales = Region(tab, Sites("Sites.Locales.Heading"));
                var changed = await tab.AnswerAsync(
                    () => Ui.PressAsync(Ui.ButtonIn(locales, Sites(action) + Ui.Format(Sites("Sites.Locales.Which"), "de")), "Enter"),
                    method,
                    tab.ManagementUrl($"sites/{id}/locales/de"));
                changed.Ok.ShouldBeTrue();
                await Expect(locales.Locator(".sites-message")).Not.ToBeEmptyAsync();
                await Expect(await FormLocaleAsync(tab, "de")).ToHaveCountAsync(shown);
            }

            // The question names the site and says that the admin leaves it; the admin is then in the default site.
            await Ui.PressAsync(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = MultiSite("MultiSite.Sites.Menu"), Exact = true }), "Enter");
            await Expect(Heading(tab, MultiSite("MultiSite.Sites.Page.Heading"))).ToBeVisibleAsync();
            var dialog = await tab.OpenDialogAsync(
                () => Ui.PressAsync(Ui.ButtonIn(main, MultiSite("MultiSite.Sites.Delete") + Ui.Format(MultiSite("MultiSite.Sites.Which"), name)), "Enter"),
                MultiSite("MultiSite.Delete.Title"));
            await Expect(dialog).ToContainTextAsync(Ui.Format(MultiSite("MultiSite.Delete.Text"), name, slug));
            await Expect(dialog).ToContainTextAsync(MultiSite("MultiSite.Delete.Current"));
            var deleted = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(dialog, MultiSite("MultiSite.Delete.Confirm")), "Enter"), "DELETE", tab.ManagementUrl($"sites/{id}"));
            deleted.Status.ShouldBe(204);

            await Expect(tab.SiteSwitcher).ToHaveValueAsync(Host.Sites[0].Id);
            await Expect(tab.SiteSwitcher.Locator("option", new LocatorLocatorOptions { HasText = name })).ToHaveCountAsync(0);
            await Expect(tab.Announced(Ui.Format(MultiSite("MultiSite.Delete.Moved"), Host.Sites[0].Name))).ToBeAttachedAsync();
            await Expect(Heading(tab, MultiSite("MultiSite.Sites.Page.Heading"))).ToBeVisibleAsync();
            browser.PolicyViolations.ShouldBeEmpty();
        }
        finally
        {
            // A case that failed before its delete leaves no third site for the other classes; after the delete this is a 4xx.
            if (id is not null)
            {
                using var management = await Host.ManagementClientAsync();
                using var removed = await management.DeleteAsync(new Uri($"api/management/v1/sites/{id}", UriKind.Relative), CancellationToken.None);
            }
        }
    }

    // The sites page, through the sidebar.
    private static async Task OpenSitesAsync(AdminTab tab)
    {
        await Ui.PressAsync(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText("Administration.Menu.Sites"), Exact = true }), "Enter");
        await Expect(Heading(tab, Repository.AdministrationHeading("Sites"))).ToBeVisibleAsync();
    }

    // The link of a locale in the locale switcher of a new article's form, reached through the sidebar and the list.
    private static async Task<ILocator> FormLocaleAsync(AdminTab tab, string code)
    {
        await Ui.PressAsync(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Article", Exact = true }), "Enter");
        await Ui.PressAsync(Ui.ButtonIn(tab.Page.Locator("main"), Repository.AdminText("Content.List.New")), "Enter");
        await Expect(tab.Page).ToHaveURLAsync(new Regex("/content/article/new"));
        await Expect(tab.Page.Locator("main .content-form-toolbar")).ToBeVisibleAsync();
        return tab.Page.Locator($"main .content-locales a[hreflang='{code}']");
    }

    [Fact]
    public async Task ACallerWhoHoldsSitesManageInASiteOnlySeesItsLocalesAndNoneOfTheMultiSitePages()
    {
        var role = $"Site manager {Mark}";
        using (var management = await Host.ManagementClientAsync())
        {
            var grants = new JsonArray([.. ((string[])["schema.read", "sites.manage"]).Select(permission => (JsonNode)new JsonObject { ["permission"] = permission })]);
            using var created = await management.PostAsJsonAsync(
                new Uri("api/management/v1/roles", UriKind.Relative),
                new JsonObject { ["name"] = role, ["siteId"] = Host.Sites[0].Id, ["grants"] = grants },
                TestContext.Current.CancellationToken);
            created.EnsureSuccessStatusCode();
        }

        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var half = Requests(tab);
        await tab.Page.GotoAsync(new Uri(Host.Admin, "sites").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, await Host.CreateEditorAsync(role, site: "default"));

        await Expect(Heading(tab, Repository.AdministrationHeading("Sites"))).ToBeVisibleAsync();
        await Expect(tab.Page.Locator("main table.sites-locales tbody th").Filter(new LocatorFilterOptions { HasTextRegex = new Regex("^en$") })).ToHaveCountAsync(1);
        await Expect(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = MultiSite("MultiSite.Sites.Menu"), Exact = true })).ToHaveCountAsync(0);

        await tab.TokenRequestOfAsync(() => tab.Page.GotoAsync(new Uri(Host.Admin, "multi-site/sites").AbsoluteUri));
        await Expect(tab.Heading("Shell.NoAccess.Heading")).ToBeVisibleAsync();
        half.ShouldBeEmpty();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task InASiteWhereTheMultiSitePluginIsDisabledTheHalfHasNoMenuItemItsPageIsNotFoundAndItIsNeverFetched()
    {
        using var management = await Host.ManagementClientAsync();
        var token = TestContext.Current.CancellationToken;
        using var created = await management.PostAsJsonAsync(new Uri("api/management/v1/sites", UriKind.Relative), new { name = $"Sites {Mark}", slug = Mark, defaultLocale = "en" }, token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var site = new Site(created.Headers.Location!.OriginalString.Split('/')[^1], $"Sites {Mark}", false);
        try
        {
            using (var disabled = await management.PutAsJsonAsync(new Uri($"api/management/v1/plugins/multi-site/sites/{site.Id}", UriKind.Relative), new { enabled = false }, token))
            {
                disabled.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            var browser = await OpenBrowserAsync();
            var tab = await browser.NewTabAsync();
            var half = Requests(tab);
            await tab.Page.GotoAsync(new Uri(Host.Admin, "system/components").AbsoluteUri);
            await browser.SignInOnTheHostedPageAsync(tab, await Host.CreateEditorAsync("Super Admin"));
            var entry = tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = MultiSite("MultiSite.Sites.Menu"), Exact = true });
            await Expect(entry).ToBeVisibleAsync();

            await tab.SwitchSiteAsync(site);
            await Expect(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText("Administration.Menu.Sites"), Exact = true })).ToBeVisibleAsync();
            await Expect(entry).ToHaveCountAsync(0);

            // The tab keeps the site across the load of the half's page.
            await tab.TokenRequestOfAsync(() => tab.Page.GotoAsync(new Uri(Host.Admin, "multi-site/sites").AbsoluteUri));
            await Expect(tab.Heading("Shell.NotFound.Heading")).ToBeVisibleAsync();
            await Expect(tab.SiteSwitcher).ToHaveValueAsync(site.Id);
            half.ShouldBeEmpty();
            browser.PolicyViolations.ShouldBeEmpty();
        }
        finally
        {
            using var removed = await management.DeleteAsync(new Uri($"api/management/v1/sites/{site.Id}", UriKind.Relative), CancellationToken.None);
        }
    }

    private static string MultiSite(string key) =>
        MultiSiteTexts.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"MultiSiteResources.resx has no key {key}.");

    private static string Sites(string key) => Repository.AdministrationText("Sites", key);

    private static ILocator Heading(AdminTab tab, string name) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = name, Exact = true, Level = 1 });

    // A card of a page, a region named by its heading.
    private static ILocator Region(AdminTab tab, string name) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Region, new LocatorGetByRoleOptions { Name = name, Exact = true });

    // A field by its label, which ends with a marker when a value is required.
    private static ILocator Field(ILocator scope, string label) => scope.GetByLabel(new Regex($@"^{Regex.Escape(label)}\b"));

    private static ConcurrentQueue<string> Requests(AdminTab tab)
    {
        var fetched = new ConcurrentQueue<string>();
        tab.Page.Request += (_, request) =>
        {
            if (MultiSiteHalf.IsMatch(new Uri(request.Url).AbsolutePath))
            {
                fetched.Enqueue(request.Url);
            }
        };
        return fetched;
    }
}
