using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// Core's administration pages are in an assembly of their own, which is not in the first download: it is fetched on the
// first visit of one of its routes that the user may open, once per page load, and not for a route the user may not open
// (spec 2.6, 4.15). The five halves beside the media half are fetched only by a visit of one of their contributions.
public sealed class AdministrationTests(EndToEndHost host) : BrowserTest(host)
{
    // The assemblies as the publish names them, with the fingerprint, and none of the files beside them.
    private static readonly Regex Administration = new(@"/_framework/FluentCMS\.Admin\.Administration(\.[0-9a-z]+)?\.wasm\z", RegexOptions.CultureInvariant);

    private static readonly string[] HeldInTheSite = ["schema.read", "users.manage", "roles.manage"];

    private static readonly Regex OtherHalves = new(@"/_framework/FluentCMS\.Plugins\.(Webhooks|Mcp|AiAssist|MultiSite|Search)\.Admin(\.[0-9a-z]+)?\.wasm\z", RegexOptions.CultureInvariant);

    [Fact]
    public async Task TheAdministrationAssemblyIsNotInTheFirstDownloadAndIsFetchedOnceOnTheFirstVisitOfOneOfItsRoutes()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var administration = Requests(tab, Administration);
        var halves = Requests(tab, OtherHalves);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, await Host.CreateEditorAsync("Super Admin"));

        // A Super Admin sees the entries of every area, and nothing of them was fetched with the first page.
        foreach (var area in (string[])["Users", "Roles", "ApiKeys", "Members", "Plugins", "Jobs", "Diagnostics", "Settings", "Sites"])
        {
            await Expect(Entry(tab, area)).ToBeVisibleAsync();
        }

        administration.ShouldBeEmpty();

        await Entry(tab, "Users").ClickAsync();
        await Expect(Heading(tab, "Users")).ToBeVisibleAsync();
        administration.Count.ShouldBe(1);

        await Entry(tab, "Roles").ClickAsync();
        await Expect(Heading(tab, "Roles")).ToBeVisibleAsync();
        await Entry(tab, "Diagnostics").ClickAsync();
        await Expect(Heading(tab, "Diagnostics")).ToBeVisibleAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, "system/diagnostics").AbsoluteUri);
        administration.Count.ShouldBe(1);

        // A reload on a route of an area is a new page load, which fetches the assembly for that route, once.
        await tab.ReloadAsync();
        await Expect(Heading(tab, "Diagnostics")).ToBeVisibleAsync();
        await Entry(tab, "Jobs").ClickAsync();
        await Expect(Heading(tab, "Jobs")).ToBeVisibleAsync();
        administration.Count.ShouldBe(2);

        halves.ShouldBeEmpty();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACallerWhoHoldsUsersManageThroughASiteRoleOnlySeesNoUsersEntryAndTheRouteAnswersNoAccessTypedAndReloaded()
    {
        var role = $"Site administrator {Mark}";
        using (var management = await Host.ManagementClientAsync())
        {
            var grants = new JsonArray([.. HeldInTheSite.Select(permission => (JsonNode)new JsonObject { ["permission"] = permission })]);
            using var created = await management.PostAsJsonAsync(
                new Uri("api/management/v1/roles", UriKind.Relative),
                new JsonObject { ["name"] = role, ["siteId"] = Host.Sites[0].Id, ["grants"] = grants },
                TestContext.Current.CancellationToken);
            created.EnsureSuccessStatusCode();
        }

        var user = await Host.CreateEditorAsync(role, site: "default");
        var browser = await OpenBrowserAsync();

        // The address is typed: the sign-in returns to it.
        var tab = await browser.NewTabAsync();
        var administration = Requests(tab, Administration);
        await tab.Page.GotoAsync(new Uri(Host.Admin, "users").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, user);

        await Expect(tab.Heading("Shell.NoAccess.Heading")).ToBeVisibleAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, "users").AbsoluteUri);
        await Expect(Entry(tab, "Roles")).ToBeVisibleAsync();
        await Expect(Entry(tab, "Users")).ToHaveCountAsync(0);
        administration.ShouldBeEmpty();

        await tab.ReloadAsync();
        await Expect(tab.Heading("Shell.NoAccess.Heading")).ToBeVisibleAsync();
        await Expect(Entry(tab, "Users")).ToHaveCountAsync(0);
        administration.ShouldBeEmpty();

        // The grant of the site opens the page of a site area, which fetches the assembly; the users page, which the
        // router now knows, still answers no access.
        await Entry(tab, "Roles").ClickAsync();
        await Expect(Heading(tab, "Roles")).ToBeVisibleAsync();
        administration.Count.ShouldBe(1);
        await tab.Page.GoBackAsync();
        await Expect(tab.Heading("Shell.NoAccess.Heading")).ToBeVisibleAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, "users").AbsoluteUri);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static ILocator Entry(AdminTab tab, string area) =>
        tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText($"Administration.Menu.{area}"), Exact = true });

    private static ILocator Heading(AdminTab tab, string area) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Repository.AdministrationText(area, $"{area}.Page.Heading"), Exact = true });

    private static ConcurrentQueue<string> Requests(AdminTab tab, Regex file)
    {
        var fetched = new ConcurrentQueue<string>();
        tab.Page.Request += (_, request) =>
        {
            if (file.IsMatch(new Uri(request.Url).AbsolutePath))
            {
                fetched.Enqueue(request.Url);
            }
        };
        return fetched;
    }
}
