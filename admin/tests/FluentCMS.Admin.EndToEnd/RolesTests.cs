using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.EndToEnd.Ui;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The roles pages (spec 4.15): an administrator makes a role from the catalogue by keyboard, with grants of core, of a
// content type and of a plugin, changes it, and the role is given to a user, which the server's refusal to delete it shows.
public sealed class RolesTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task AnAdministratorMakesARoleWithGrantsOfCoreOfAContentTypeAndOfAPluginThatIsThenGivenToAUser()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "roles", await Host.CreateEditorAsync("Super Admin"));
        var main = tab.Page.Locator("main");
        await Expect(Heading(main, Repository.AdministrationHeading("Roles"))).ToBeVisibleAsync();

        await PressAsync(main.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Text("Roles.List.New"), Exact = true }), "Enter");
        await Expect(Heading(main, Text("Roles.Editor.NewHeading"))).ToBeVisibleAsync();
        var role = $"Translators {Mark}";
        await main.GetByLabel(Text("Roles.Editor.Name")).FillAsync(role);
        await AddGrantAsync(main, "core", "schema.read");
        await AddGrantAsync(main, "content", "content.article.read");
        await AddGrantAsync(main, "plugin", "media.read");
        await Expect(main.Locator("[data-grant='media.read']")).ToContainTextAsync(Format(Text("Roles.Grants.OfPlugin"), "media"));

        var created = await tab.AnswerAsync(() => PressAsync(ButtonIn(main, Text("Roles.Editor.Create")), "Enter"), "POST", tab.ManagementUrl("roles"));
        var id = await CreatedIdAsync(created, "roles");
        await Expect(Heading(main, role)).ToBeVisibleAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, $"roles/{id:D}").AbsoluteUri);
        await Expect(main.Locator("[data-grant]")).ToHaveCountAsync(3);

        var renamed = $"Article translators {Mark}";
        await main.GetByLabel(Text("Roles.Editor.Name")).FillAsync(renamed);
        var saved = await tab.AnswerAsync(() => PressAsync(ButtonIn(main, Text("Roles.Editor.Save")), "Enter"), "PUT", tab.ManagementUrl($"roles/{id:D}"));
        saved.Status.ShouldBe(200);
        await Expect(main.GetByText(Text("Roles.Editor.Saved"), new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();

        // The users page is another issue's: the host's command gives the role, as an operator does.
        await Host.CreateEditorAsync(renamed, site: "default");

        await PressAsync(ButtonIn(main, Text("Roles.Editor.Delete")), "Enter");
        var refused = await tab.AnswerAsync(() => PressAsync(ButtonIn(main, Text("Roles.Editor.DeleteConfirm")), "Enter"), "DELETE", tab.ManagementUrl($"roles/{id:D}"));
        refused.Status.ShouldBe(409);
        await Expect(main.GetByText(Text("Roles.Editor.InUse"), new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, $"roles/{id:D}").AbsoluteUri);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key) => Repository.AdministrationText("Roles", key);

    private static ILocator Heading(ILocator main, string name) =>
        main.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = name, Exact = true, Level = 1 });

    private static async Task AddGrantAsync(ILocator main, string source, string permission)
    {
        await main.GetByLabel(Text("Roles.Grants.Source"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync(source);
        await main.GetByLabel(Text("Roles.Grants.Permission"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync(permission);
        await PressAsync(ButtonIn(main, Text("Roles.Grants.Add")), "Enter");
        await Expect(main.Locator($"[data-grant='{permission}']")).ToBeVisibleAsync();
    }
}
